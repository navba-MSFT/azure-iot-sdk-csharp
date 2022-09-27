﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Amqp;
using Microsoft.Azure.Amqp.Framing;
using Microsoft.Azure.Devices.Authentication;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.Devices.Provisioning.Client
{
    /// <summary>
    /// Represents the AMQP protocol implementation for the provisioning transport handler.
    /// </summary>
    public class ProvisioningTransportHandlerAmqp : ProvisioningTransportHandler
    {
        // This polling interval is the default time between checking if the device has reached a terminal state in its registration process
        // DPS will generally send a retry-after header that overrides this default value though.
        private static readonly TimeSpan s_defaultOperationPollingInterval = TimeSpan.FromSeconds(2);

        private static readonly TimeSpan s_timeoutConstant = TimeSpan.FromMinutes(1);

        private TimeSpan? _retryAfter;

        /// <summary>
        /// Creates an instance of the ProvisioningTransportHandlerAmqp class using the specified fallback type.
        /// </summary>
        /// <param name="transportProtocol">The protocol over which the AMQP transport communicates (i.e., TCP or web socket).</param>
        public ProvisioningTransportHandlerAmqp(
            ProvisioningClientTransportProtocol transportProtocol = ProvisioningClientTransportProtocol.Tcp)
        {
            TransportProtocol = transportProtocol;
            bool useWebSocket = TransportProtocol == ProvisioningClientTransportProtocol.WebSocket;
            Port = useWebSocket ? AmqpWebSocketConstants.Port : AmqpConstants.DefaultSecurePort;
        }

        /// <summary>
        /// The protocol over which the AMQP transport communicates (i.e., TCP or web socket).
        /// </summary>
        public ProvisioningClientTransportProtocol TransportProtocol { get; private set; }

        /// <summary>
        /// Registers a device described by the message. Because the AMQP library does not accept cancellation tokens, the provided cancellation token
        /// will only be checked for cancellation between AMQP operations. The timeout will be respected during the AMQP operations.
        /// </summary>
        /// <param name="message">The provisioning message.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The registration result.</returns>
        public override async Task<DeviceRegistrationResult> RegisterAsync(
            ProvisioningTransportRegisterRequest message,
            CancellationToken cancellationToken)
        {
            if (Logging.IsEnabled)
                Logging.Enter(this, $"{nameof(ProvisioningTransportHandlerAmqp)}.{nameof(RegisterAsync)}");

            Argument.AssertNotNull(message, nameof(message));

            // We need to create a LinkedTokenSource to include both the default timeout and the cancellation token
            // AMQP library started supporting CancellationToken starting from version 2.5.5
            // To preserve current behavior, we will honor both the legacy timeout and the cancellation token parameter.
            using var timeoutTokenSource = new CancellationTokenSource(s_timeoutConstant);
            using var cancellationTokenSourceBundle = CancellationTokenSource.CreateLinkedTokenSource(timeoutTokenSource.Token, cancellationToken);

            CancellationToken bundleCancellationToken = cancellationTokenSourceBundle.Token;

            bundleCancellationToken.ThrowIfCancellationRequested();

            try
            {
                AmqpAuthStrategy authStrategy;

                if (message.Authentication is AuthenticationProviderTpm tpm)
                {
                    authStrategy = new AmqpAuthStrategyTpm(tpm);
                }
                else if (message.Authentication is AuthenticationProviderX509 x509)
                {
                    authStrategy = new AmqpAuthStrategyX509(x509);
                }
                else if (message.Authentication is AuthenticationProviderSymmetricKey key)
                {
                    authStrategy = new AmqpAuthStrategySymmetricKey(key);
                }
                else
                {
                    throw new NotSupportedException(
                        $"{nameof(message.Authentication)} must be of type {nameof(AuthenticationProviderTpm)}, " +
                        $"{nameof(AuthenticationProviderX509)} or {nameof(AuthenticationProviderSymmetricKey)}");
                }

                if (Logging.IsEnabled)
                    Logging.Associate(authStrategy, this);

                bool useWebSocket = TransportProtocol == ProvisioningClientTransportProtocol.WebSocket;

                var builder = new UriBuilder
                {
                    Scheme = useWebSocket ? AmqpWebSocketConstants.Scheme : AmqpConstants.SchemeAmqps,
                    Host = message.GlobalDeviceEndpoint,
                    Port = Port,
                };

                string registrationId = message.Authentication.GetRegistrationId();
                string linkEndpoint = $"{message.IdScope}/registrations/{registrationId}";

                using AmqpClientConnection connection = authStrategy.CreateConnection(builder.Uri, message.IdScope);

                await authStrategy.OpenConnectionAsync(connection, useWebSocket, Proxy, RemoteCertificateValidationCallback, bundleCancellationToken).ConfigureAwait(false);
                bundleCancellationToken.ThrowIfCancellationRequested();

                await CreateLinksAsync(
                        connection,
                        linkEndpoint,
                        message.ProductInfo,
                        bundleCancellationToken)
                    .ConfigureAwait(false);

                bundleCancellationToken.ThrowIfCancellationRequested();

                string correlationId = Guid.NewGuid().ToString();
                DeviceRegistration deviceRegistration = (message.Payload != null && message.Payload.Length > 0)
                    ? new DeviceRegistration(new JRaw(message.Payload))
                    : null;

                RegistrationOperationStatus operation = await RegisterDeviceAsync(
                        connection,
                        correlationId,
                        deviceRegistration,
                        bundleCancellationToken)
                    .ConfigureAwait(false);

                // Poll with operationId until registration complete.
                int attempts = 0;
                string operationId = operation.OperationId;

                // Poll with operationId until registration complete.
                while (string.CompareOrdinal(operation.Status, RegistrationOperationStatus.OperationStatusAssigning) == 0
                    || string.CompareOrdinal(operation.Status, RegistrationOperationStatus.OperationStatusUnassigned) == 0)
                {
                    bundleCancellationToken.ThrowIfCancellationRequested();

                    await Task.Delay(
                            operation.RetryAfter ?? RetryJitter.GenerateDelayWithJitterForRetry(s_defaultOperationPollingInterval),
                            bundleCancellationToken)
                        .ConfigureAwait(false);

                    try
                    {
                        operation = await OperationStatusLookupAsync(
                                connection,
                                operationId,
                                correlationId,
                                bundleCancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (DeviceProvisioningClientException e) when (e.IsTransient)
                    {
                        operation.RetryAfter = _retryAfter;
                    }

                    attempts++;
                }

                if (string.CompareOrdinal(operation.Status, RegistrationOperationStatus.OperationStatusAssigned) == 0)
                {
                    authStrategy.SaveCredentials(operation);
                }

                await connection.CloseAsync(bundleCancellationToken).ConfigureAwait(false);

                return operation.RegistrationState;
            }
            catch (Exception ex) when (ex is not DeviceProvisioningClientException)
            {
                if (Logging.IsEnabled)
                    Logging.Error(this, $"{nameof(ProvisioningTransportHandlerAmqp)} threw exception {ex}", nameof(RegisterAsync));

                throw new DeviceProvisioningClientException($"AMQP transport exception", ex, true);
            }
            finally
            {
                if (Logging.IsEnabled)
                    Logging.Exit(this, $"{nameof(ProvisioningTransportHandlerAmqp)}.{nameof(RegisterAsync)}");
            }
        }

        private static async Task CreateLinksAsync(AmqpClientConnection connection, string linkEndpoint, string productInfo, CancellationToken cancellationToken)
        {
            AmqpClientSession amqpDeviceSession = connection.CreateSession();
            await amqpDeviceSession.OpenAsync(cancellationToken).ConfigureAwait(false);

            AmqpClientLink amqpReceivingLink = amqpDeviceSession.CreateReceivingLink(linkEndpoint);

            amqpReceivingLink.AddClientVersion(productInfo);
            amqpReceivingLink.AddApiVersion(ClientApiVersionHelper.ApiVersion);

            await amqpReceivingLink.OpenAsync(cancellationToken).ConfigureAwait(false);

            AmqpClientLink amqpSendingLink = amqpDeviceSession.CreateSendingLink(linkEndpoint);

            amqpSendingLink.AddClientVersion(productInfo);
            amqpSendingLink.AddApiVersion(ClientApiVersionHelper.ApiVersion);

            await amqpSendingLink.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task<RegistrationOperationStatus> RegisterDeviceAsync(
            AmqpClientConnection client,
            string correlationId,
            DeviceRegistration deviceRegistration,
            CancellationToken cancellationToken)
        {
            AmqpMessage amqpMessage = null;

            try
            {
                if (deviceRegistration == null)
                {
                    amqpMessage = AmqpMessage.Create(new MemoryStream(), true);
                }
                else
                {
                    var customContentStream = new MemoryStream(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(deviceRegistration)));
                    amqpMessage = AmqpMessage.Create(customContentStream, true);
                }

                amqpMessage.Properties.CorrelationId = correlationId;
                amqpMessage.ApplicationProperties.Map[MessageApplicationPropertyNames.OperationType] = DeviceOperations.Register;
                amqpMessage.ApplicationProperties.Map[MessageApplicationPropertyNames.ForceRegistration] = false;

                Outcome outcome = await client.AmqpSession.SendingLink
                    .SendMessageAsync(
                        amqpMessage,
                        new ArraySegment<byte>(Guid.NewGuid().ToByteArray()),
                        cancellationToken)
                    .ConfigureAwait(false);

                ValidateOutcome(outcome);

                AmqpMessage amqpResponse = await client.AmqpSession.ReceivingLink.ReceiveMessageAsync(cancellationToken).ConfigureAwait(false);
                client.AmqpSession.ReceivingLink.AcceptMessage(amqpResponse);

                using var streamReader = new StreamReader(amqpResponse.BodyStream);
                string jsonResponse = await streamReader
                    .ReadToEndAsync()
                    .ConfigureAwait(false);
                RegistrationOperationStatus status = JsonConvert.DeserializeObject<RegistrationOperationStatus>(jsonResponse);
                status.RetryAfter = ProvisioningErrorDetailsAmqp.GetRetryAfterFromApplicationProperties(amqpResponse, s_defaultOperationPollingInterval);
                return status;
            }
            finally
            {
                amqpMessage?.Dispose();
            }
        }

        private async Task<RegistrationOperationStatus> OperationStatusLookupAsync(
            AmqpClientConnection client,
            string operationId,
            string correlationId,
            CancellationToken cancellationToken)
        {
            using var amqpMessage = AmqpMessage.Create(new AmqpValue { Value = DeviceOperations.GetOperationStatus });

            amqpMessage.Properties.CorrelationId = correlationId;
            amqpMessage.ApplicationProperties.Map[MessageApplicationPropertyNames.OperationType] = DeviceOperations.GetOperationStatus;
            amqpMessage.ApplicationProperties.Map[MessageApplicationPropertyNames.OperationId] = operationId;

            Outcome outcome = await client.AmqpSession.SendingLink
                .SendMessageAsync(
                    amqpMessage,
                    new ArraySegment<byte>(Guid.NewGuid().ToByteArray()),
                    cancellationToken)
                .ConfigureAwait(false);

            ValidateOutcome(outcome);

            AmqpMessage amqpResponse = await client.AmqpSession.ReceivingLink
                .ReceiveMessageAsync(cancellationToken)
                .ConfigureAwait(false);

            client.AmqpSession.ReceivingLink.AcceptMessage(amqpResponse);

            using var streamReader = new StreamReader(amqpResponse.BodyStream);
            string jsonResponse = await streamReader.ReadToEndAsync().ConfigureAwait(false);
            RegistrationOperationStatus status = JsonConvert.DeserializeObject<RegistrationOperationStatus>(jsonResponse);
            status.RetryAfter = ProvisioningErrorDetailsAmqp.GetRetryAfterFromApplicationProperties(amqpResponse, s_defaultOperationPollingInterval);

            return status;
        }

        private void ValidateOutcome(Outcome outcome)
        {
            if (outcome is Rejected rejected)
            {
                try
                {
                    ProvisioningErrorDetailsAmqp errorDetails = JsonConvert.DeserializeObject<ProvisioningErrorDetailsAmqp>(rejected.Error.Description);
                    // status code has an extra 3 trailing digits as a sub-code, so turn this into a standard 3 digit status code
                    int statusCode = errorDetails.ErrorCode / 1000;
                    bool isTransient = statusCode >= (int)HttpStatusCode.InternalServerError || statusCode == 429;
                    if (isTransient)
                    {
                        errorDetails.RetryAfter = ProvisioningErrorDetailsAmqp.GetRetryAfterFromRejection(rejected, s_defaultOperationPollingInterval);
                        _retryAfter = errorDetails.RetryAfter;
                    }

                    throw new DeviceProvisioningClientException(
                        rejected.Error.Description,
                        null,
                        (HttpStatusCode)statusCode,
                        errorDetails.ErrorCode,
                        errorDetails.TrackingId);
                }
                catch (JsonException ex)
                {
                    if (Logging.IsEnabled)
                        Logging.Error(
                            this,
                            $"{nameof(ProvisioningTransportHandlerAmqp)} server returned malformed error response." +
                                $"Parsing error: {ex}. Server response: {rejected.Error.Description}",
                            nameof(RegisterAsync));

                    throw new DeviceProvisioningClientException(
                        $"AMQP transport exception: malformed server error message: '{rejected.Error.Description}'",
                        ex,
                        false);
                }
            }
        }
    }
}
