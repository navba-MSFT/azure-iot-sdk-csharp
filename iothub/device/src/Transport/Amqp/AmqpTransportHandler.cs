﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Amqp;
using Microsoft.Azure.Devices.Client.Exceptions;
using Microsoft.Azure.Devices.Client.Transport.AmqpIot;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;

namespace Microsoft.Azure.Devices.Client.Transport.Amqp
{
    internal class AmqpTransportHandler : TransportHandler
    {
        private const int ResponseTimeoutInSeconds = 300;
        private readonly TimeSpan _operationTimeout;
        protected AmqpUnit _amqpUnit;
        private readonly Func<IDictionary<string, object>, Task> _onDesiredStatePatchListener;
        private readonly object _lock = new object();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<AmqpMessage>> _twinResponseCompletions = new ConcurrentDictionary<string, TaskCompletionSource<AmqpMessage>>();
        private bool _closed;

        static AmqpTransportHandler()
        {
            try
            {
                AmqpTrace.Provider = new AmqpIotTransportLog();
            }
            catch (Exception ex)
            {
                // Do not throw from static ctor.
                if (Logging.IsEnabled)
                    Logging.Error(null, ex, nameof(AmqpTransportHandler));
            }
        }

        internal AmqpTransportHandler(
            PipelineContext context,
            IotHubConnectionString connectionString,
            AmqpTransportSettings transportSettings,
            Func<MethodRequestInternal, Task> onMethodCallback = null,
            Func<IDictionary<string, object>, Task> onDesiredStatePatchReceivedCallback = null,
            Func<string, Message, Task> onModuleMessageReceivedCallback = null,
            Func<Message, Task> onDeviceMessageReceivedCallback = null)
            : base(context, transportSettings)
        {
            _operationTimeout = transportSettings.OperationTimeout;
            _onDesiredStatePatchListener = onDesiredStatePatchReceivedCallback;
            IDeviceIdentity deviceIdentity = new DeviceIdentity(connectionString, transportSettings, context.ProductInfo, context.ClientOptions);
            _amqpUnit = AmqpUnitManager.GetInstance().CreateAmqpUnit(
                deviceIdentity,
                onMethodCallback,
                TwinMessageListener,
                onModuleMessageReceivedCallback,
                onDeviceMessageReceivedCallback,
                OnDisconnected);

            if (Logging.IsEnabled)
                Logging.Associate(this, _amqpUnit, nameof(_amqpUnit));
        }

        private void OnDisconnected()
        {
            if (!_closed)
            {
                lock (_lock)
                {
                    if (!_closed)
                    {
                        OnTransportDisconnected();
                    }
                }
            }
        }

        public override bool IsUsable => !_disposed;

        public override async Task OpenAsync(TimeoutHelper timeoutHelper)
        {
            if (Logging.IsEnabled)
                Logging.Enter(this, timeoutHelper, nameof(OpenAsync));

            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                _closed = false;
            }

            try
            {
                using var cts = new CancellationTokenSource(timeoutHelper.GetRemainingTime());
                await _amqpUnit.OpenAsync(cts.Token).ConfigureAwait(false);
            }
            finally
            {
                if (Logging.IsEnabled)
                    Logging.Exit(this, timeoutHelper, nameof(OpenAsync));
            }
        }

        public override async Task OpenAsync(CancellationToken cancellationToken)
        {
            if (Logging.IsEnabled)
                Logging.Enter(this, cancellationToken, nameof(OpenAsync));

            cancellationToken.ThrowIfCancellationRequested();
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                _closed = false;
            }

            try
            {
                using var ctb = new CancellationTokenBundle(_operationTimeout, cancellationToken);
                await _amqpUnit.OpenAsync(ctb.Token).ConfigureAwait(false);
            }
            finally
            {
                if (Logging.IsEnabled)
                    Logging.Exit(this, cancellationToken, nameof(OpenAsync));
            }
        }

        public override async Task CloseAsync(CancellationToken cancellationToken)
        {
            if (Logging.IsEnabled)
                Logging.Enter(this, nameof(CloseAsync));

            lock (_lock)
            {
                _closed = true;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var ctb = new CancellationTokenBundle(_operationTimeout, cancellationToken);
                await _amqpUnit.CloseAsync(ctb.Token).ConfigureAwait(false);
            }
            finally
            {
                OnTransportClosedGracefully();
                if (Logging.IsEnabled)
                    Logging.Exit(this, nameof(CloseAsync));
            }
        }

        public override async Task SendEventAsync(MessageBase message, CancellationToken cancellationToken)
        {
            if (Logging.IsEnabled)
                Logging.Enter(this, message, cancellationToken, nameof(SendEventAsync));

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var ctb = new CancellationTokenBundle(_operationTimeout, cancellationToken);
                AmqpIotOutcome amqpIotOutcome = await _amqpUnit.SendEventAsync(message, ctb.Token).ConfigureAwait(false);

                amqpIotOutcome?.ThrowIfNotAccepted();
            }
            finally
            {
                if (Logging.IsEnabled)
                    Logging.Exit(this, message, cancellationToken, nameof(SendEventAsync));
            }
        }

        public override async Task SendEventAsync(IEnumerable<MessageBase> messages, CancellationToken cancellationToken)
        {
            if (Logging.IsEnabled)
                Logging.Enter(this, messages, cancellationToken, nameof(SendEventAsync));

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var ctb = new CancellationTokenBundle(_operationTimeout, cancellationToken);
                await _amqpUnit.SendEventsAsync(messages, ctb.Token).ConfigureAwait(false);
            }
            finally
            {
                if (Logging.IsEnabled)
                    Logging.Exit(this, messages, cancellationToken, nameof(SendEventAsync));
            }
        }

        public override async Task<Message> ReceiveAsync(TimeoutHelper timeoutHelper)
        {
            if (Logging.IsEnabled)
                Logging.Enter(this, timeoutHelper, timeoutHelper.GetRemainingTime(), nameof(ReceiveAsync));

            using var cts = new CancellationTokenSource(timeoutHelper.GetRemainingTime());
            Message message = await _amqpUnit.ReceiveMessageAsync(cts.Token).ConfigureAwait(false);

            if (Logging.IsEnabled)
                Logging.Exit(this, timeoutHelper, timeoutHelper.GetRemainingTime(), nameof(ReceiveAsync));

            return message;
        }

        public override async Task<Message> ReceiveAsync(CancellationToken cancellationToken)
        {
            if (Logging.IsEnabled)
                Logging.Enter(this, cancellationToken, nameof(ReceiveAsync));

            Message message;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var ctb = new CancellationTokenBundle(_transportSettings.DefaultReceiveTimeout, cancellationToken);
                message = await _amqpUnit.ReceiveMessageAsync(ctb.Token).ConfigureAwait(false);

                if (message != null)
                {
                    break;
                }
            }

            if (Logging.IsEnabled)
                Logging.Exit(this, cancellationToken, cancellationToken, nameof(ReceiveAsync));

            return message;
        }

        public override async Task EnableReceiveMessageAsync(CancellationToken cancellationToken)
        {
            if (Logging.IsEnabled)
                Logging.Enter(this, cancellationToken, nameof(EnableReceiveMessageAsync));

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var ctb = new CancellationTokenBundle(_operationTimeout, cancellationToken);
                await _amqpUnit.EnableReceiveMessageAsync(ctb.Token).ConfigureAwait(false);
            }
            finally
            {
                if (Logging.IsEnabled)
                    Logging.Exit(this, cancellationToken, nameof(EnableReceiveMessageAsync));
            }
        }

        // This method is added to ensure that over MQTT devices can receive messages that were sent when it was disconnected.
        // This behavior is available by default over AMQP, so no additional implementation is required here.
        public override Task EnsurePendingMessagesAreDeliveredAsync(CancellationToken cancellationToken)
        {
            return TaskHelpers.CompletedTask;
        }

        public override async Task DisableReceiveMessageAsync(CancellationToken cancellationToken)
        {
            if (Logging.IsEnabled)
                Logging.Enter(this, cancellationToken, nameof(DisableReceiveMessageAsync));

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var ctb = new CancellationTokenBundle(_operationTimeout, cancellationToken);
                await _amqpUnit.DisableReceiveMessageAsync(ctb.Token).ConfigureAwait(false);
            }
            finally
            {
                if (Logging.IsEnabled)
                    Logging.Exit(this, cancellationToken, nameof(DisableReceiveMessageAsync));
            }
        }

        public override async Task EnableMethodsAsync(CancellationToken cancellationToken)
        {
            if (Logging.IsEnabled)
                Logging.Enter(this, cancellationToken, nameof(EnableMethodsAsync));

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var ctb = new CancellationTokenBundle(_operationTimeout, cancellationToken);

                await _amqpUnit.EnableMethodsAsync(ctb.Token).ConfigureAwait(false);
            }
            finally
            {
                if (Logging.IsEnabled)
                    Logging.Exit(this, cancellationToken, nameof(EnableMethodsAsync));
            }
        }

        public override async Task DisableMethodsAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (Logging.IsEnabled)
                    Logging.Enter(this, cancellationToken, nameof(DisableMethodsAsync));

                cancellationToken.ThrowIfCancellationRequested();

                using var ctb = new CancellationTokenBundle(_operationTimeout, cancellationToken);
                await _amqpUnit.DisableMethodsAsync(ctb.Token).ConfigureAwait(false);
            }
            finally
            {
                Logging.Exit(this, cancellationToken, nameof(DisableMethodsAsync));
            }
        }

        public override async Task SendMethodResponseAsync(MethodResponseInternal methodResponse, CancellationToken cancellationToken)
        {
            if (Logging.IsEnabled)
                Logging.Enter(this, methodResponse, cancellationToken, nameof(SendMethodResponseAsync));

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var ctb = new CancellationTokenBundle(_operationTimeout, cancellationToken);
                AmqpIotOutcome amqpIotOutcome = await _amqpUnit
                    .SendMethodResponseAsync(methodResponse, ctb.Token)
                    .ConfigureAwait(false);

                if (amqpIotOutcome != null)
                {
                    amqpIotOutcome.ThrowIfNotAccepted();
                }
            }
            finally
            {
                if (Logging.IsEnabled)
                    Logging.Exit(this, methodResponse, cancellationToken, nameof(SendMethodResponseAsync));
            }
        }

        public override async Task EnableTwinPatchAsync(CancellationToken cancellationToken)
        {
            if (Logging.IsEnabled)
                Logging.Enter(this, cancellationToken, nameof(EnableTwinPatchAsync));

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string correlationId = AmqpTwinMessageType.Put + Guid.NewGuid().ToString();

                using var ctb = new CancellationTokenBundle(_operationTimeout, cancellationToken);
                await _amqpUnit
                    .SendTwinMessageAsync(AmqpTwinMessageType.Put, correlationId, null, ctb.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (Logging.IsEnabled)
                    Logging.Exit(this, cancellationToken, nameof(EnableTwinPatchAsync));
            }
        }

        public override async Task DisableTwinPatchAsync(CancellationToken cancellationToken)
        {
            try
            {
                Logging.Enter(this, cancellationToken, nameof(DisableTwinPatchAsync));

                cancellationToken.ThrowIfCancellationRequested();

                using var ctb = new CancellationTokenBundle(_operationTimeout, cancellationToken);
                await _amqpUnit.DisableTwinLinksAsync(ctb.Token).ConfigureAwait(false);
            }
            finally
            {
                if (Logging.IsEnabled)
                    Logging.Exit(this, cancellationToken, nameof(DisableTwinPatchAsync));
            }
        }

        public override async Task<T> GetClientTwinPropertiesAsync<T>(CancellationToken cancellationToken)
        {
            if (Logging.IsEnabled)
                Logging.Enter(this, cancellationToken, nameof(GetClientTwinPropertiesAsync));

            try
            {
                await EnableTwinPatchAsync(cancellationToken).ConfigureAwait(false);
                AmqpMessage responseFromService = await RoundTripTwinMessageAsync(AmqpTwinMessageType.Get, null, cancellationToken)
                    .ConfigureAwait(false);

                if (responseFromService == null)
                {
                    throw new InvalidOperationException("Service rejected the message");
                }

                // We will use UTF-8 for decoding the service response. This is because UTF-8 is the only currently supported encoding format.
                using var reader = new StreamReader(responseFromService.BodyStream, Encoding.UTF8);
                string body = reader.ReadToEnd();

                try
                {
                    // We will use NewtonSoft Json to deserialize the service response to the appropriate type; i.e. Twin for non-convention-based operation
                    // and ClientProperties for convention-based operations.
                    return JsonConvert.DeserializeObject<T>(body);
                }
                catch (JsonReaderException ex)
                {
                    if (Logging.IsEnabled)
                        Logging.Error(this, $"Failed to parse Twin JSON: {ex}. Message body: '{body}'");

                    throw;
                }
            }
            finally
            {
                if (Logging.IsEnabled)
                    Logging.Exit(this, cancellationToken, nameof(GetClientTwinPropertiesAsync));
            }
        }

        public override async Task<ClientPropertiesUpdateResponse> SendClientTwinPropertyPatchAsync(Stream reportedProperties, CancellationToken cancellationToken)
        {
            if (Logging.IsEnabled)
                Logging.Enter(this, reportedProperties, cancellationToken, nameof(SendClientTwinPropertyPatchAsync));

            try
            {
                await EnableTwinPatchAsync(cancellationToken).ConfigureAwait(false);
                AmqpMessage responseFromService = await RoundTripTwinMessageAsync(AmqpTwinMessageType.Patch, reportedProperties, cancellationToken).ConfigureAwait(false);

                long updatedVersion = GetVersion(responseFromService);
                return new ClientPropertiesUpdateResponse
                {
                    Version = updatedVersion,
                };
            }
            finally
            {
                if (Logging.IsEnabled)
                    Logging.Exit(this, reportedProperties, cancellationToken, nameof(SendClientTwinPropertyPatchAsync));
            }
        }

        private async Task<AmqpMessage> RoundTripTwinMessageAsync(
            AmqpTwinMessageType amqpTwinMessageType,
            Stream reportedProperties,
            CancellationToken cancellationToken)
        {
            if (Logging.IsEnabled)
                Logging.Enter(this, cancellationToken, nameof(RoundTripTwinMessageAsync));

            string correlationId = amqpTwinMessageType + Guid.NewGuid().ToString();
            AmqpMessage response = default;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var taskCompletionSource = new TaskCompletionSource<AmqpMessage>();
                _twinResponseCompletions[correlationId] = taskCompletionSource;

                using var ctb = new CancellationTokenBundle(_operationTimeout, cancellationToken);
                await _amqpUnit.SendTwinMessageAsync(amqpTwinMessageType, correlationId, reportedProperties, ctb.Token).ConfigureAwait(false);

                Task<AmqpMessage> receivingTask = taskCompletionSource.Task;

                if (await Task
                    .WhenAny(receivingTask, Task.Delay(TimeSpan.FromSeconds(ResponseTimeoutInSeconds), cancellationToken))
                    .ConfigureAwait(false) == receivingTask)
                {
                    if (receivingTask.Exception?.InnerException != null)
                    {
                        throw receivingTask.Exception.InnerException;
                    }

                    // Task completed within timeout.
                    // Consider that the task may have faulted or been canceled.
                    // We re-await the task so that any exceptions/cancellation is re-thrown.
                    response = await receivingTask.ConfigureAwait(false);
                }
                else
                {
                    // Timeout happen
                    throw new TimeoutException();
                }
            }
            finally
            {
                _twinResponseCompletions.TryRemove(correlationId, out _);
                if (Logging.IsEnabled)
                    Logging.Exit(this, cancellationToken, nameof(RoundTripTwinMessageAsync));
            }

            return response;
        }

        public override async Task EnableEventReceiveAsync(bool isAnEdgeModule, CancellationToken cancellationToken)
        {
            // If an AMQP transport is opened as a module twin instead of an Edge module we need
            // to enable the deviceBound operations instead of the event receiver link
            if (isAnEdgeModule)
            {
                if (Logging.IsEnabled)
                    Logging.Enter(this, cancellationToken, nameof(EnableEventReceiveAsync));

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using var ctb = new CancellationTokenBundle(_operationTimeout, cancellationToken);

                    await _amqpUnit.EnableEventReceiveAsync(ctb.Token).ConfigureAwait(false);
                }
                finally
                {
                    if (Logging.IsEnabled)
                        Logging.Exit(this, cancellationToken, nameof(EnableEventReceiveAsync));
                }
            }
            else
            {
                await EnableReceiveMessageAsync(cancellationToken).ConfigureAwait(false);
            }

        }

        public override Task CompleteAsync(string lockToken, CancellationToken cancellationToken)
        {
            if (Logging.IsEnabled)
                Logging.Enter(this, lockToken, cancellationToken, nameof(CompleteAsync));

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return DisposeMessageAsync(lockToken, AmqpIotDisposeActions.Accepted, cancellationToken);
            }
            finally
            {
                if (Logging.IsEnabled)
                    Logging.Exit(this, lockToken, cancellationToken, nameof(CompleteAsync));
            }
        }

        public override Task AbandonAsync(string lockToken, CancellationToken cancellationToken)
        {
            if (Logging.IsEnabled)
                Logging.Enter(this, lockToken, cancellationToken, nameof(AbandonAsync));

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return DisposeMessageAsync(lockToken, AmqpIotDisposeActions.Released, cancellationToken);
            }
            finally
            {
                if (Logging.IsEnabled)
                    Logging.Exit(this, lockToken, cancellationToken, nameof(AbandonAsync));
            }
        }

        public override Task RejectAsync(string lockToken, CancellationToken cancellationToken)
        {
            if (Logging.IsEnabled)
                Logging.Enter(this, lockToken, cancellationToken, nameof(RejectAsync));

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return DisposeMessageAsync(lockToken, AmqpIotDisposeActions.Rejected, cancellationToken);
            }
            finally
            {
                if (Logging.IsEnabled)
                    Logging.Exit(this, lockToken, cancellationToken, nameof(RejectAsync));
            }
        }

        private async Task DisposeMessageAsync(string lockToken, AmqpIotDisposeActions outcome, CancellationToken cancellationToken)
        {
            if (Logging.IsEnabled)
                Logging.Enter(this, outcome, nameof(DisposeMessageAsync));

            try
            {
                // Currently, the same mechanism is used for sending feedback for C2D messages and events received by modules.
                // However, devices only support C2D messages (they cannot receive events), and modules only support receiving events
                // (they cannot receive C2D messages). So we use this to distinguish whether to dispose the message (i.e. send outcome on)
                // the DeviceBoundReceivingLink or the EventsReceivingLink.
                // If this changes (i.e. modules are able to receive C2D messages, or devices are able to receive telemetry), this logic
                // will have to be updated.
                using var ctb = new CancellationTokenBundle(_operationTimeout, cancellationToken);

                AmqpIotOutcome disposeOutcome = await _amqpUnit.DisposeMessageAsync(lockToken, outcome, ctb.Token).ConfigureAwait(false);
                disposeOutcome.ThrowIfError();
            }
            finally
            {
                if (Logging.IsEnabled)
                    Logging.Exit(this, outcome, nameof(DisposeMessageAsync));
            }
        }

        private void TwinMessageListener(AmqpMessage responseFromService, string correlationId, IotHubException ex = default)
        {
            if (correlationId == null)
            {
                // This is desired property updates, so invoke the callback with TwinCollection.
                using var reader = new StreamReader(responseFromService.BodyStream, Encoding.UTF8);
                string responseBody = reader.ReadToEnd();

                _onDesiredStatePatchListener(JsonConvert.DeserializeObject<IDictionary<string, object>>(responseBody));
            }
            else if (correlationId.StartsWith(AmqpTwinMessageType.Get.ToString(), StringComparison.OrdinalIgnoreCase)
                || correlationId.StartsWith(AmqpTwinMessageType.Patch.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                if (Logging.IsEnabled)
                    Logging.Info(this, $"Received a response for operation with correlation Id {correlationId}.", nameof(TwinMessageListener));

                // For Get and Patch, complete the task.
                if (_twinResponseCompletions.TryRemove(correlationId, out TaskCompletionSource<AmqpMessage> task))
                {
                    if (ex == default)
                    {
                        task.SetResult(responseFromService);
                    }
                    else
                    {
                        task.SetException(ex);
                    }
                }
                else
                {
                    // This can happen if we received a message from service with correlation Id that was not sent by SDK or does not exist in dictionary.
                    if (Logging.IsEnabled)
                        Logging.Error(this, $"Could not remove correlation id {correlationId} to complete the task awaiter for a twin operation.", nameof(TwinMessageListener));
                }
            }
            else if (correlationId.StartsWith(AmqpTwinMessageType.Put.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                // This is an acknowledgment received from service for subscribing to desired property updates.
                if (Logging.IsEnabled)
                    Logging.Info(this, $"Subscribed for twin successfully with a correlation Id of {correlationId}.", nameof(TwinMessageListener));
            }
            else
            {
                // This can happen if we received a message from service with correlation Id that was not sent by SDK or does not exist in dictionary.
                if (Logging.IsEnabled)
                    Logging.Error(this, $"Received an unexpected response from service with correlation Id {correlationId}.", nameof(TwinMessageListener));
            }
        }

        internal static long GetVersion(AmqpMessage response)
        {
            if (response != null)
            {
                if (response.MessageAnnotations.Map.TryGetValue(AmqpIotConstants.ResponseVersionName, out long version))
                {
                    return version;
                }
            }

            return -1;
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (Logging.IsEnabled)
                {
                    Logging.Enter(this, $"{nameof(DefaultDelegatingHandler)}.Disposed={_disposed}; disposing={disposing}", $"{nameof(AmqpTransportHandler)}.{nameof(Dispose)}");
                }

                lock (_lock)
                {
                    if (!_disposed)
                    {
                        base.Dispose(disposing);
                        if (disposing)
                        {
                            _closed = true;
                            AmqpUnitManager.GetInstance()?.RemoveAmqpUnit(_amqpUnit);
                            _disposed = true;
                        }
                    }

                    // the _disposed flag is inherited from the base class DefaultDelegatingHandler and is finally set to null there.
                }
            }
            finally
            {
                if (Logging.IsEnabled)
                {
                    Logging.Exit(this, $"{nameof(DefaultDelegatingHandler)}.Disposed={_disposed}; disposing={disposing}", $"{nameof(AmqpTransportHandler)}.{nameof(Dispose)}");
                }
            }
        }
    }
}
