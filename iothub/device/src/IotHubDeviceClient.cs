// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using DotNetty.Transport.Channels;
using Microsoft.Azure.Devices.Client.Exceptions;
using Microsoft.Azure.Devices.Client.Transport;

namespace Microsoft.Azure.Devices.Client
{
    /// <summary>
    /// Contains methods that a device can use to send messages to and receive from the service.
    /// </summary>
    /// <threadsafety static="true" instance="true" />
    public class IotHubDeviceClient : IDisposable
#if NETSTANDARD2_1_OR_GREATER
        , IAsyncDisposable
#endif
    {
        /// <summary>
        /// Default operation timeout.
        /// </summary>
        public const uint DefaultOperationTimeoutInMilliseconds = 4 * 60 * 1000;

        private IotHubDeviceClient(InternalClient internalClient)
        {
            InternalClient = internalClient ?? throw new ArgumentNullException(nameof(internalClient));

            if (InternalClient.IotHubConnectionString?.ModuleId != null)
            {
                throw new ArgumentException("A module Id was specified in the connection string - please use ModuleClient for modules.");
            }

            if (Logging.IsEnabled)
                Logging.Associate(this, this, internalClient, nameof(IotHubDeviceClient));
        }

        /// <summary>
        /// Creates a disposable DeviceClient from the specified parameters, that uses AMQP transport protocol.
        /// </summary>
        /// <param name="hostname">The fully-qualified DNS host name of IoT hub</param>
        /// <param name="authenticationMethod">The authentication method that is used</param>
        /// <param name="options">The options that allow configuration of the device client instance during initialization.</param>
        /// <returns>A disposable DeviceClient instance</returns>
        public static IotHubDeviceClient Create(string hostname, IAuthenticationMethod authenticationMethod, IotHubClientOptions options = default)
        {
            return Create(() => ClientFactory.Create(hostname, authenticationMethod, options));
        }

        /// <summary>
        /// Creates a disposable DeviceClient using AMQP transport from the specified connection string
        /// </summary>
        /// <param name="connectionString">Connection string for the IoT hub (including DeviceId)</param>
        /// <param name="options">The options that allow configuration of the device client instance during initialization.</param>
        /// <returns>A disposable DeviceClient instance</returns>
        public static IotHubDeviceClient CreateFromConnectionString(string connectionString, IotHubClientOptions options = default)
        {
            Argument.AssertNotNullOrWhiteSpace(connectionString, nameof(connectionString));
            return Create(() => ClientFactory.CreateFromConnectionString(connectionString, options));
        }

        private static IotHubDeviceClient Create(Func<InternalClient> internalClientCreator)
        {
            return new IotHubDeviceClient(internalClientCreator());
        }

        internal IDelegatingHandler InnerHandler
        {
            get => InternalClient.InnerHandler;
            set => InternalClient.InnerHandler = value;
        }

        internal InternalClient InternalClient { get; private set; }

        /// <summary>
        /// Diagnostic sampling percentage value, [0-100];
        /// 0 means no message will carry on diagnostics info
        /// </summary>
        public int DiagnosticSamplingPercentage
        {
            get => InternalClient.DiagnosticSamplingPercentage;
            set => InternalClient.DiagnosticSamplingPercentage = value;
        }

        /// <summary>
        /// Stores custom product information that will be appended to the user agent string that is sent to IoT hub.
        /// </summary>
        public string ProductInfo
        {
            get => InternalClient.ProductInfo;
            set => InternalClient.ProductInfo = value;
        }

        /// <summary>
        /// Sets a new delegate for the connection status changed callback. If a delegate is already associated,
        /// it will be replaced with the new delegate. Note that this callback will never be called if the client is configured to use
        /// HTTP, as that protocol is stateless.
        /// <param name="statusChangesHandler">The name of the method to associate with the delegate.</param>
        /// </summary>
        public void SetConnectionStatusChangesHandler(ConnectionStatusChangesHandler statusChangesHandler) =>
            InternalClient.SetConnectionStatusChangesHandler(statusChangesHandler);

        /// <summary>
        /// Set a callback that will be called whenever the client receives a state update
        /// (desired or reported) from the service.
        /// Set callback value to null to clear.
        /// </summary>
        /// <remarks>
        /// This has the side-effect of subscribing to the PATCH topic on the service.
        /// </remarks>
        /// <param name="callback">Callback to call after the state update has been received and applied.</param>
        /// <param name="userContext">Context object that will be passed into callback.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// TODO:azabbasi
        /// <exception cref="OperationCanceledException">Thrown when the operation has been canceled.</exception>
        /// <exception cref="TaskCanceledException">Thrown when the operation has been canceled.</exception>
        public Task SetDesiredPropertyUpdateCallbackAsync(
            DesiredPropertyUpdateCallback callback,
            object userContext,
            CancellationToken cancellationToken = default) =>
            InternalClient.SetDesiredPropertyUpdateCallbackAsync(callback, userContext, cancellationToken);

        /// <summary>
        /// Sets a new delegate for the named method. If a delegate is already associated with
        /// the named method, it will be replaced with the new delegate.
        /// A method handler can be unset by passing a null MethodCallback.
        /// <param name="methodName">The name of the method to associate with the delegate.</param>
        /// <param name="methodHandler">The delegate to be used when a method with the given name is called by the cloud service.</param>
        /// <param name="userContext">generic parameter to be interpreted by the client code.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <exception cref="OperationCanceledException">Thrown when the operation has been canceled.</exception>
        /// <exception cref="TaskCanceledException">Thrown when the operation has been canceled.</exception>
        /// </summary>
        public Task SetMethodHandlerAsync(
            string methodName,
            MethodCallback methodHandler,
            object userContext,
            CancellationToken cancellationToken = default) =>
            InternalClient.SetMethodHandlerAsync(methodName, methodHandler, userContext, cancellationToken);

        /// <summary>
        /// Sets a new delegate that is called for a method that doesn't have a delegate registered for its name.
        /// If a default delegate is already registered it will replace with the new delegate.
        /// A method handler can be unset by passing a null MethodCallback.
        /// </summary>
        /// <param name="methodHandler">The delegate to be used when a method is called by the cloud service and there is
        /// no delegate registered for that method name.</param>
        /// <param name="userContext">Generic parameter to be interpreted by the client code.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <exception cref="OperationCanceledException">Thrown when the operation has been canceled.</exception>
        /// <exception cref="TaskCanceledException">Thrown when the operation has been canceled.</exception>
        public Task SetMethodDefaultHandlerAsync(MethodCallback methodHandler, object userContext, CancellationToken cancellationToken = default) =>
            InternalClient.SetMethodDefaultHandlerAsync(methodHandler, userContext, cancellationToken);

        /// <summary>
        /// Sets the retry policy used in the operation retries.
        /// The change will take effect after any in-progress operations.
        /// </summary>
        /// <param name="retryPolicy">The retry policy. The default is
        /// <c>new ExponentialBackoff(int.MaxValue, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100));</c></param>
        public void SetRetryPolicy(IRetryPolicy retryPolicy)
        {
            InternalClient.SetRetryPolicy(retryPolicy);
        }

        /// <summary>
        /// Explicitly open the DeviceClient instance.
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <exception cref="OperationCanceledException">Thrown when the operation has been canceled.</exception>
        /// </summary>
        public Task OpenAsync(CancellationToken cancellationToken = default) => InternalClient.OpenAsync(cancellationToken);

        /// <summary>
        /// Close the DeviceClient instance.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <exception cref="OperationCanceledException">Thrown when the operation has been canceled.</exception>
        public Task CloseAsync(CancellationToken cancellationToken = default) => InternalClient.CloseAsync(cancellationToken);

        /// <summary>
        /// Sends an event to a hub
        /// </summary>
        /// <param name="message">The message to send. Should be disposed after sending.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required parameter is null.</exception>
        /// <exception cref="OperationCanceledException">Thrown if the service does not respond to the request before the
        /// expiration of the passed <see cref="CancellationToken"/>. If a cancellation token is not supplied to the
        /// operation call, a cancellation token with an expiration time of 4 minutes is used.
        /// </exception>
        /// <exception cref="IotHubCommunicationException">Thrown if the client encounters a transient retriable exception. </exception>
        /// <exception cref="IotHubCommunicationException">Thrown when the operation has been canceled. The inner exception will be
        /// <see cref="OperationCanceledException"/>.</exception>
        /// <exception cref="SocketException">Thrown if a socket error occurs.</exception>
        /// <exception cref="WebSocketException">Thrown if an error occurs when performing an operation on a WebSocket connection.</exception>
        /// <exception cref="IOException">Thrown if an I/O error occurs.</exception>
        /// <exception cref="ClosedChannelException">Thrown if the MQTT transport layer closes unexpectedly.</exception>
        /// <exception cref="IotHubException">Thrown if an error occurs when communicating with IoT hub service.
        /// If <see cref="IotHubException.IsTransient"/> is set to <c>true</c> then it is a transient exception.
        /// If <see cref="IotHubException.IsTransient"/> is set to <c>false</c> then it is a non-transient exception.</exception>
        /// <remarks>
        /// In case of a transient issue, retrying the operation should work. In case of a non-transient issue, inspect
        /// the error details and take steps accordingly.
        /// Please note that the list of exceptions is not exhaustive.
        /// </remarks>
        public Task SendEventAsync(Message message, CancellationToken cancellationToken = default) =>
            InternalClient.SendEventAsync(message, cancellationToken);

        /// <summary>
        /// Sends a batch of events to IoT hub. Use AMQP or HTTPs for a true batch operation. MQTT will just send the messages
        /// one after the other.
        /// </summary>
        /// <param name="messages">An <see cref="IEnumerable{Message}"/> set of message objects.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <exception cref="IotHubCommunicationException">Thrown when the operation has been canceled. The inner exception will be
        /// <see cref="OperationCanceledException"/>.</exception>
        public Task SendEventBatchAsync(IEnumerable<Message> messages, CancellationToken cancellationToken = default) =>
            InternalClient.SendEventBatchAsync(messages, cancellationToken);

        /// <summary>
        /// Retrieve the device twin properties for the current device.
        /// For the complete device twin object, use Microsoft.Azure.Devices.RegistryManager.GetTwinAsync(string deviceId).
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <exception cref="IotHubCommunicationException">Thrown when the operation has been canceled. The inner exception will be
        /// <see cref="OperationCanceledException"/>.</exception>
        /// <returns>The device twin object for the current device</returns>
        public Task<Twin> GetTwinAsync(CancellationToken cancellationToken = default) => InternalClient.GetTwinAsync(cancellationToken);

        /// <summary>
        /// Push reported property changes up to the service.
        /// </summary>
        /// <param name="reportedProperties">Reported properties to push</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <exception cref="IotHubCommunicationException">Thrown when the operation has been canceled. The inner exception will be
        /// <see cref="OperationCanceledException"/>.</exception>
        public Task UpdateReportedPropertiesAsync(TwinCollection reportedProperties, CancellationToken cancellationToken = default) =>
            InternalClient.UpdateReportedPropertiesAsync(reportedProperties, cancellationToken);

        /// <summary>
        /// Receive a message from the device queue using the cancellation token.
        /// After handling a received message, a client should call <see cref="CompleteMessageAsync(Message, CancellationToken)"/>,
        /// <see cref="AbandonMessageAsync(Message, CancellationToken)"/>, or <see cref="RejectMessageAsync(Message, CancellationToken)"/>,
        /// and then dispose the message.
        /// </summary>
        /// <remarks>
        /// You cannot reject or abandon messages over MQTT protocol. For more details, see
        /// <see href="https://docs.microsoft.com/azure/iot-hub/iot-hub-devguide-messages-c2d#the-cloud-to-device-message-life-cycle"/>.
        /// </remarks>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <exception cref="IotHubCommunicationException">Thrown when the operation has been canceled. The inner exception will be
        /// <see cref="OperationCanceledException"/>.</exception>
        /// <returns>The received message or null if there was no message until cancellation token has expired</returns>
        public Task<Message> ReceiveMessageAsync(CancellationToken cancellationToken = default) => InternalClient.ReceiveMessageAsync(cancellationToken);

        /// <summary>
        /// Sets a new delegate for receiving a message from the device queue using a cancellation token.
        /// After handling a received message, a client should call <see cref="CompleteMessageAsync(Message, CancellationToken)"/>,
        /// <see cref="AbandonMessageAsync(Message, CancellationToken)"/>, or <see cref="RejectMessageAsync(Message, CancellationToken)"/>,
        /// and then dispose the message.
        /// If a null delegate is passed, it will disable the callback triggered on receiving messages from the service.
        /// <param name="messageHandler">The delegate to be used when a could to device message is received by the client.</param>
        /// <param name="userContext">Generic parameter to be interpreted by the client code.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// </summary>
        public Task SetReceiveMessageHandlerAsync(
            ReceiveMessageCallback messageHandler,
            object userContext,
            CancellationToken cancellationToken = default) =>
            InternalClient.SetReceiveMessageHandlerAsync(messageHandler, userContext, cancellationToken);

        /// <summary>
        /// Deletes a received message from the device queue.
        /// </summary>
        /// <param name="lockToken">The message lockToken.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <exception cref="IotHubCommunicationException">Thrown when the operation has been canceled.
        /// The inner exception will be <see cref="OperationCanceledException"/>.</exception>
        public Task CompleteMessageAsync(string lockToken, CancellationToken cancellationToken = default) =>
            InternalClient.CompleteMessageAsync(lockToken, cancellationToken);

        /// <summary>
        /// Deletes a received message from the device queue.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <exception cref="IotHubCommunicationException">Thrown when the operation has been canceled.
        /// The inner exception will be <see cref="OperationCanceledException"/>.</exception>
        public Task CompleteMessageAsync(Message message, CancellationToken cancellationToken = default) =>
            InternalClient.CompleteMessageAsync(message, cancellationToken);

        /// <summary>
        /// Puts a received message back onto the device queue.
        /// </summary>
        /// <remarks>
        /// You cannot reject or abandon messages over MQTT protocol. For more details, see
        /// <see href="https://docs.microsoft.com/azure/iot-hub/iot-hub-devguide-messages-c2d#the-cloud-to-device-message-life-cycle"/>.
        /// </remarks>
        /// <param name="lockToken">The message lockToken.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <exception cref="IotHubCommunicationException">Thrown when the operation has been canceled.
        /// The inner exception will be <see cref="OperationCanceledException"/>.</exception>
        public Task AbandonMessageAsync(string lockToken, CancellationToken cancellationToken = default) =>
            InternalClient.AbandonMessageAsync(lockToken, cancellationToken);

        /// <summary>
        /// Puts a received message back onto the device queue.
        /// </summary>
        /// <remarks>
        /// You cannot reject or abandon messages over MQTT protocol. For more details, see
        /// <see href="https://docs.microsoft.com/azure/iot-hub/iot-hub-devguide-messages-c2d#the-cloud-to-device-message-life-cycle"/>.
        /// </remarks>
        /// <param name="message">The message to abandon.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <exception cref="IotHubCommunicationException">Thrown when the operation has been canceled.
        /// The inner exception will be <see cref="OperationCanceledException"/>.</exception>
        public Task AbandonMessageAsync(Message message, CancellationToken cancellationToken = default) =>
            InternalClient.AbandonMessageAsync(message, cancellationToken);

        /// <summary>
        /// Deletes a received message from the device queue and indicates to the server that the message could not be processed.
        /// </summary>
        /// <remarks>
        /// You cannot reject or abandon messages over MQTT protocol. For more details, see
        /// <see href="https://docs.microsoft.com/azure/iot-hub/iot-hub-devguide-messages-c2d#the-cloud-to-device-message-life-cycle"/>.
        /// </remarks>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <param name="lockToken">The message lockToken.</param>
        /// <exception cref="IotHubCommunicationException">Thrown when the operation has been canceled.
        /// The inner exception will be <see cref="OperationCanceledException"/>.</exception>
        public Task RejectMessageAsync(string lockToken, CancellationToken cancellationToken = default) =>
            InternalClient.RejectMessageAsync(lockToken, cancellationToken);

        /// <summary>
        /// Deletes a received message from the device queue and indicates to the server that the message could not be processed.
        /// </summary>
        /// <remarks>
        /// You cannot reject or abandon messages over MQTT protocol. For more details, see
        /// <see href="https://docs.microsoft.com/azure/iot-hub/iot-hub-devguide-messages-c2d#the-cloud-to-device-message-life-cycle"/>.
        /// </remarks>
        /// <param name="message">The message to reject.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <exception cref="IotHubCommunicationException">Thrown when the operation has been canceled.
        /// The inner exception will be <see cref="OperationCanceledException"/>.</exception>
        public Task RejectMessageAsync(Message message, CancellationToken cancellationToken = default) =>
            InternalClient.RejectMessageAsync(message, cancellationToken);

        /// <summary>
        /// Get a file upload SAS URI which the Azure Storage SDK can use to upload a file to blob for this device
        /// See <see href="https://docs.microsoft.com/azure/iot-hub/iot-hub-devguide-file-upload#initialize-a-file-upload">this documentation for more details</see>.
        /// </summary>
        /// <param name="request">The request details for getting the SAS URI, including the destination blob name.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The file upload details to be used with the Azure Storage SDK in order to upload a file from this device.</returns>
        public Task<FileUploadSasUriResponse> GetFileUploadSasUriAsync(
            FileUploadSasUriRequest request,
            CancellationToken cancellationToken = default) =>
            InternalClient.GetFileUploadSasUriAsync(request, cancellationToken);

        /// <summary>
        /// Notify IoT hub that a device's file upload has finished. See
        /// <see href="https://docs.microsoft.com/azure/iot-hub/iot-hub-devguide-file-upload#notify-iot-hub-of-a-completed-file-upload">this documentation for more details</see>.
        /// </summary>
        /// <param name="notification">The notification details, including if the file upload succeeded.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public Task CompleteFileUploadAsync(FileUploadCompletionNotification notification, CancellationToken cancellationToken = default) =>
            InternalClient.CompleteFileUploadAsync(notification, cancellationToken);

        /// <summary>
        /// Releases the unmanaged resources used by the DeviceClient and optionally disposes of the managed resources.
        /// </summary>
        /// <remarks>
        /// The method <see cref="CloseAsync(CancellationToken)"/> should be called before disposing.
        /// </remarks>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

#if NETSTANDARD2_1_OR_GREATER
        // IAsyncDisposable is available in .NET Standard 2.1 and above

        /// <summary>
        /// Disposes the client in an asynchronous way. See <see cref="IAsyncDisposable"/> for more information.
        /// </summary>
        /// <remarks>
        /// Includes a call to <see cref="CloseAsync(CancellationToken)"/>.
        /// </remarks>
        /// <example>
        /// <c>
        /// await using var client = DeviceClient.CreateFromConnectionString(...);
        /// </c>
        /// or
        /// <c>
        /// var client = DeviceClient.CreateFromConnectionString(...);
        /// try
        /// {
        ///     // do work
        /// }
        /// finally
        /// {
        ///     await client.DisposeAsync();
        /// }
        /// </c>
        /// </example>
        [SuppressMessage("Usage", "CA1816:Dispose methods should call SuppressFinalize", Justification = "SuppressFinalize is called by Dispose(), which this method calls.")]
        public async ValueTask DisposeAsync()
        {
            await CloseAsync().ConfigureAwait(false);
            Dispose();
        }

#endif

        /// <summary>
        /// Releases the unmanaged resources used by the DeviceClient and allows for any derived class to override and
        /// provide custom implementation.
        /// </summary>
        /// <param name="disposing">Setting to true will release both managed and unmanaged resources. Setting to
        /// false will only release the unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                InternalClient?.Dispose();
            }
        }
    }
}