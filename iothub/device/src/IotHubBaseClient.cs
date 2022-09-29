﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client.Transport;

namespace Microsoft.Azure.Devices.Client
{
    /// <summary>
    /// Contains methods that a client can use to send messages to and receive messages from the service,
    /// respond to direct method invocations from the service, and send and receive twin property updates.
    /// </summary>
    public abstract class IotHubBaseClient : IDisposable
    {
        private readonly SemaphoreSlim _methodsSemaphore = new(1, 1);
        private readonly SemaphoreSlim _twinDesiredPropertySemaphore = new(1, 1);
        private readonly SemaphoreSlim _receiveMessageSemaphore = new(1, 1);

        private volatile Func<Message, Task<MessageAcknowledgement>> _receiveMessageCallback;

        // Connection status change information
        private volatile Action<ConnectionStatusInfo> _connectionStatusChangeCallback;

        // Method callback information
        private bool _isDeviceMethodEnabled;

        private volatile Func<DirectMethodRequest, Task<DirectMethodResponse>> _deviceDefaultMethodCallback;

        // Twin property update request callback information
        private bool _twinPatchSubscribedWithService;

        private Func<TwinCollection, Task> _desiredPropertyUpdateCallback;

        private protected readonly IotHubClientOptions _clientOptions;

        internal IotHubBaseClient(
            IotHubConnectionCredentials iotHubConnectionCredentials,
            IotHubClientOptions iotHubClientOptions)
        {
            if (Logging.IsEnabled)
                Logging.Enter(this, iotHubClientOptions?.TransportSettings, nameof(IotHubBaseClient) + "_ctor");

            // Make sure client options is initialized.
            if (iotHubClientOptions == default)
            {
                iotHubClientOptions = new();
            }

            IotHubConnectionCredentials = iotHubConnectionCredentials;
            _clientOptions = iotHubClientOptions;

            ClientPipelineBuilder pipelineBuilder = BuildPipeline();

            PipelineContext = new PipelineContext
            {
                IotHubConnectionCredentials = IotHubConnectionCredentials,
                ProductInfo = _clientOptions.UserAgentInfo,
                IotHubClientTransportSettings = _clientOptions.TransportSettings,
                ModelId = _clientOptions.ModelId,
                MethodCallback = OnMethodCalledAsync,
                DesiredPropertyUpdateCallback = OnDesiredStatePatchReceived,
                ConnectionStatusChangeHandler = OnConnectionStatusChanged,
                MessageEventCallback = OnMessageReceivedAsync,
            };

            InnerHandler = pipelineBuilder.Build(PipelineContext);

            if (Logging.IsEnabled)
                Logging.Exit(this, _clientOptions.TransportSettings, nameof(IotHubBaseClient) + "_ctor");
        }

        /// <summary>
        /// The latest connection status information since the last status change.
        /// </summary>
        public ConnectionStatusInfo ConnectionStatusInfo { get; private set; } = new();

        internal IotHubConnectionCredentials IotHubConnectionCredentials { get; private set; }

        internal IDelegatingHandler InnerHandler { get; set; }

        private protected PipelineContext PipelineContext { get; private set; }

        /// <summary>
        /// Sets the retry policy used in the operation retries.
        /// The change will take effect after any in-progress operations.
        /// </summary>
        /// <param name="retryPolicy">The retry policy. The default is
        /// <c>new ExponentialBackoff(int.MaxValue, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100));</c></param>
        public void SetRetryPolicy(IRetryPolicy retryPolicy)
        {
            RetryDelegatingHandler retryDelegatingHandler = GetDelegateHandler<RetryDelegatingHandler>();
            if (retryDelegatingHandler == null)
            {
                throw new NotSupportedException();
            }

            retryDelegatingHandler.SetRetryPolicy(retryPolicy);
        }

        /// <summary>
        /// Sets a new callback for receiving connection status change notifications. If a callback is already associated,
        /// it will be replaced with the new callback.
        /// </summary>
        /// <param name="statusChangeHandler">The callback for the connection status change notifications.</param>
        public void SetConnectionStatusChangeCallback(Action<ConnectionStatusInfo> statusChangeHandler)
        {
            if (Logging.IsEnabled)
                Logging.Info(this, statusChangeHandler, nameof(SetConnectionStatusChangeCallback));

            _connectionStatusChangeCallback = statusChangeHandler;
        }

        /// <summary>
        /// Open the client instance. Must be done before any operation can begin.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <exception cref="OperationCanceledException">Thrown when the operation has been canceled.</exception>
        public async Task OpenAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await InnerHandler.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends an event to IoT hub. The client instance must be opened already.
        /// </summary>
        /// <remarks>
        /// In case of a transient issue, retrying the operation should work. In case of a non-transient issue, inspect
        /// the error details and take steps accordingly.
        /// Please note that the list of exceptions is not exhaustive.
        /// </remarks>
        /// <param name="message">The message to send.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required parameter is null.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation has been canceled.</exception>
        /// <exception cref="InvalidOperationException">Thrown if the client instance is not opened already.</exception>
        /// <exception cref="SocketException">Thrown if a socket error occurs.</exception>
        /// <exception cref="WebSocketException">Thrown if an error occurs when performing an operation on a WebSocket connection.</exception>
        /// <exception cref="IOException">Thrown if an I/O error occurs.</exception>
        /// <exception cref="IotHubClientException">Thrown if an error occurs when communicating with IoT hub service.</exception>
        public async Task SendEventAsync(Message message, CancellationToken cancellationToken = default)
        {
            Argument.AssertNotNull(message, nameof(message));
            cancellationToken.ThrowIfCancellationRequested();

            if (_clientOptions?.SdkAssignsMessageId == SdkAssignsMessageId.WhenUnset && message.MessageId == null)
            {
                message.MessageId = Guid.NewGuid().ToString();
            }

            await InnerHandler.SendEventAsync(message, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a batch of events to IoT hub. Use AMQP or HTTPs for a true batch operation. MQTT will just send the messages
        /// one after the other. The client instance must be opened already.
        /// </summary>
        /// <remarks>
        /// For more information on IoT Edge module routing for <see cref="IotHubModuleClient"/> see <see href="https://docs.microsoft.com/azure/iot-edge/module-composition?view=iotedge-2018-06#declare-routes"/>.
        /// </remarks>
        /// <param name="messages">An <see cref="IEnumerable{Message}"/> set of message objects.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <exception cref="InvalidOperationException">Thrown if the client instance is not opened already.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation has been canceled.</exception>
        public async Task SendEventBatchAsync(IEnumerable<Message> messages, CancellationToken cancellationToken = default)
        {
            Argument.AssertNotNullOrEmpty(messages, nameof(messages));
            cancellationToken.ThrowIfCancellationRequested();

            if (_clientOptions?.SdkAssignsMessageId == SdkAssignsMessageId.WhenUnset)
            {
                foreach (Message message in messages)
                {
                    message.MessageId ??= Guid.NewGuid().ToString();
                }
            }

            await InnerHandler.SendEventAsync(messages, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets a callback for receiving a message from the device or module queue using a cancellation token.
        /// This instance must be opened already.
        /// </summary>
        /// <remarks>
        /// Calling this API more than once will result in the callback set last overwriting any previously set callback.
        /// A method callback can be unset by setting <paramref name="messageCallback"/> to null.
        /// </remarks>
        /// <param name="messageCallback">The callback to be invoked when a cloud-to-device message is received by the client.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <exception cref="InvalidOperationException">Thrown if instance is not opened already.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation has been canceled.</exception>
        public async Task SetMessageCallbackAsync(
            Func<Message, Task<MessageAcknowledgement>> messageCallback,
            CancellationToken cancellationToken = default)
        {
            if (Logging.IsEnabled)
                Logging.Enter(this, messageCallback, nameof(SetMessageCallbackAsync));

            cancellationToken.ThrowIfCancellationRequested();

            // Wait to acquire the _deviceReceiveMessageSemaphore. This ensures that concurrently invoked
            // SetMessageCallbackAsync calls are invoked in a thread-safe manner.
            await _receiveMessageSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                // If a callback is already set on the client, calling SetMessageCallbackAsync
                // again will cause the callback to be overwritten.
                if (messageCallback != null)
                {
                    // If this is the first time the callback is being registered, then the telemetry downlink will be enabled.
                    await EnableReceiveMessageAsync(cancellationToken).ConfigureAwait(false);
                    _receiveMessageCallback = new Func<Message, Task<MessageAcknowledgement>>(messageCallback);
                }
                else
                {
                    // If a null callback is passed, it will disable the callback triggered on receiving messages from the service.
                    _receiveMessageCallback = null;
                    await DisableReceiveMessageAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _receiveMessageSemaphore.Release();

                if (Logging.IsEnabled)
                    Logging.Exit(this, messageCallback, nameof(SetMessageCallbackAsync));
            }
        }

        /// <summary>
        /// Sets the callback for all direct method calls from the service.
        /// This instance must be opened already.
        /// </summary>
        /// <remarks>
        /// Calling this API more than once will result in the callback set last overwriting any previously set callback.
        /// A method callback can be unset by setting <paramref name="directMethodCallback"/> to null.
        /// </remarks>
        /// <param name="directMethodCallback">The callback to be invoked when any method is invoked by the cloud service.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <exception cref="OperationCanceledException">Thrown when the operation has been canceled.</exception>
        public async Task SetDirectMethodCallbackAsync(
            Func<DirectMethodRequest, Task<DirectMethodResponse>> directMethodCallback,
            CancellationToken cancellationToken = default)
        {
            if (Logging.IsEnabled)
                Logging.Enter(this, directMethodCallback, nameof(SetDirectMethodCallbackAsync));

            cancellationToken.ThrowIfCancellationRequested();

            await _methodsSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (directMethodCallback != null)
                {
                    await HandleMethodEnableAsync(cancellationToken).ConfigureAwait(false);
                    _deviceDefaultMethodCallback = directMethodCallback;
                }
                else
                {
                    _deviceDefaultMethodCallback = null;
                    await HandleMethodDisableAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _methodsSemaphore.Release();

                if (Logging.IsEnabled)
                    Logging.Exit(this, directMethodCallback, nameof(SetDirectMethodCallbackAsync));
            }
        }

        /// <summary>
        /// Retrieve the twin properties for the current client. The client instance must be opened already.
        /// </summary>
        /// <remarks>
        /// This API gives you the client's view of the twin. For more information on twins in IoT hub, see <see href="https://docs.microsoft.com/azure/iot-hub/iot-hub-devguide-device-twins"/>.
        /// </remarks>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <exception cref="InvalidOperationException">Thrown if the client instance is not opened already.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation has been canceled.</exception>
        /// <returns>The twin object for the current client.</returns>
        public async Task<Twin> GetTwinAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // `GetTwinAsync` shall call `SendTwinGetAsync` on the transport to get the twin status.
            return await InnerHandler.SendTwinGetAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Push reported property changes up to the service.
        /// </summary>
        /// <param name="reportedProperties">Reported properties to push</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>The new version of the updated twin if the update was successful.</returns>
        /// <exception cref="OperationCanceledException">Thrown when the operation has been canceled.</exception>
        public async Task<long> UpdateReportedPropertiesAsync(TwinCollection reportedProperties, CancellationToken cancellationToken = default)
        {
            Argument.AssertNotNull(reportedProperties, nameof(reportedProperties));
            cancellationToken.ThrowIfCancellationRequested();

            // `UpdateReportedPropertiesAsync` shall call `SendTwinPatchAsync` on the transport to update the reported properties.
            return await InnerHandler.SendTwinPatchAsync(reportedProperties, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Set a callback that will be called whenever the client receives a desired state update
        /// from the service. The client instance must be opened already.
        /// </summary>
        /// <remarks>
        /// Calling this API more than once will result in the callback set last overwriting any previously set callback.
        /// A method callback can be unset by setting <paramref name="callback"/> to null.
        ///  <para>
        /// This has the side-effect of subscribing to the PATCH topic on the service.
        ///  </para>
        /// </remarks>
        /// <param name="callback">The callback to be invoked when a desired property update is received from the service.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <exception cref="OperationCanceledException">Thrown when the operation has been canceled.</exception>
        public async Task SetDesiredPropertyUpdateCallbackAsync(
            Func<TwinCollection, Task> callback,
            CancellationToken cancellationToken = default)
        {
            if (Logging.IsEnabled)
                Logging.Enter(this, callback, nameof(SetDesiredPropertyUpdateCallbackAsync));

            cancellationToken.ThrowIfCancellationRequested();

            // Wait to acquire the _twinSemaphore. This ensures that concurrently invoked SetDesiredPropertyUpdateCallbackAsync calls are invoked in a thread-safe manner.
            await _twinDesiredPropertySemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (callback != null && !_twinPatchSubscribedWithService)
                {
                    await InnerHandler.EnableTwinPatchAsync(cancellationToken).ConfigureAwait(false);
                    _twinPatchSubscribedWithService = true;
                }
                else if (callback == null && _twinPatchSubscribedWithService)
                {
                    await InnerHandler.DisableTwinPatchAsync(cancellationToken).ConfigureAwait(false);
                }

                _desiredPropertyUpdateCallback = callback;
            }
            finally
            {
                _twinDesiredPropertySemaphore.Release();

                if (Logging.IsEnabled)
                    Logging.Exit(this, callback, nameof(SetDesiredPropertyUpdateCallbackAsync));
            }
        }

        /// <summary>
        /// Close the client instance.
        /// </summary>
        /// <remarks>
        /// The instance can be re-opened after closing and before disposing.
        /// </remarks>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <exception cref="OperationCanceledException">Thrown when the operation has been canceled.</exception>
        public async Task CloseAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await InnerHandler.CloseAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the client and optionally disposes of the managed resources.
        /// </summary>
        /// <remarks>
        /// The method <see cref="CloseAsync(CancellationToken)"/> should be called before disposing.
        /// </remarks>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the client and allows for any derived class to override and
        /// provide custom implementation.
        /// </summary>
        /// <param name="disposing">Setting to true will release both managed and unmanaged resources. Setting to
        /// false will only release the unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                InnerHandler?.Dispose();
                _methodsSemaphore?.Dispose();
                _twinDesiredPropertySemaphore?.Dispose();
            }
        }

        /// <summary>
        /// The callback for handling disrupted connection/links in the transport layer.
        /// </summary>
        internal void OnConnectionStatusChanged(ConnectionStatusInfo connectionStatusInfo)
        {
            ConnectionStatus status = connectionStatusInfo.Status;
            ConnectionStatusChangeReason reason = connectionStatusInfo.ChangeReason;

            try
            {
                if (Logging.IsEnabled)
                    Logging.Enter(this, status, reason, nameof(OnConnectionStatusChanged));

                if (ConnectionStatusInfo.Status != status
                    || ConnectionStatusInfo.ChangeReason != reason)
                {
                    ConnectionStatusInfo = new ConnectionStatusInfo(status, reason);
                    _connectionStatusChangeCallback?.Invoke(ConnectionStatusInfo);
                }
            }
            finally
            {
                if (Logging.IsEnabled)
                    Logging.Exit(this, status, reason, nameof(OnConnectionStatusChanged));
            }
        }

        /// <summary>
        /// The callback for handling direct methods received from service.
        /// </summary>
        internal async Task OnMethodCalledAsync(DirectMethodRequest directMethodRequest)
        {
            if (Logging.IsEnabled)
                Logging.Enter(this, directMethodRequest?.MethodName, directMethodRequest, nameof(OnMethodCalledAsync));

            if (directMethodRequest == null)
            {
                return;
            }

            DirectMethodResponse directMethodResponse = null;

            if (_deviceDefaultMethodCallback == null)
            {
                directMethodResponse = new DirectMethodResponse((int)DirectMethodResponseStatusCode.MethodNotImplemented)
                {
                    RequestId = directMethodRequest.RequestId,
                };
            }
            else
            {
                try
                {
                    directMethodResponse = await _deviceDefaultMethodCallback
                        .Invoke(directMethodRequest)
                        .ConfigureAwait(false);

                    directMethodResponse.RequestId = directMethodRequest.RequestId;
                }
                catch (Exception ex)
                {
                    if (Logging.IsEnabled)
                        Logging.Error(this, ex, nameof(OnMethodCalledAsync));

                    directMethodResponse = new DirectMethodResponse((int)DirectMethodResponseStatusCode.UserCodeException)
                    {
                        RequestId = directMethodRequest.RequestId,
                    };
                }
            }

            await SendDirectMethodResponseAsync(directMethodResponse).ConfigureAwait(false);

            if (Logging.IsEnabled)
                Logging.Exit(this, directMethodRequest.MethodName, directMethodRequest, nameof(OnMethodCalledAsync));
        }

        internal void OnDesiredStatePatchReceived(TwinCollection patch)
        {
            if (_desiredPropertyUpdateCallback == null)
            {
                return;
            }

            if (Logging.IsEnabled)
                Logging.Info(this, patch.ToJson(), nameof(OnDesiredStatePatchReceived));

            _ = _desiredPropertyUpdateCallback.Invoke(patch);
        }

        private async Task SendDirectMethodResponseAsync(DirectMethodResponse directMethodResponse, CancellationToken cancellationToken = default)
        {
            await InnerHandler.SendMethodResponseAsync(directMethodResponse, cancellationToken).ConfigureAwait(false);
        }

        private async Task HandleMethodEnableAsync(CancellationToken cancellationToken = default)
        {
            // If currently enabled, then skip
            if (_isDeviceMethodEnabled)
            {
                return;
            }

            await InnerHandler.EnableMethodsAsync(cancellationToken).ConfigureAwait(false);
            _isDeviceMethodEnabled = true;
        }

        private async Task HandleMethodDisableAsync(CancellationToken cancellationToken = default)
        {
            // Don't disable if it is already disabled or if there are registered device methods
            if (!_isDeviceMethodEnabled || _deviceDefaultMethodCallback != null)
            {
                return;
            }

            await InnerHandler.DisableMethodsAsync(cancellationToken).ConfigureAwait(false);
            _isDeviceMethodEnabled = false;
        }

        private protected static ClientPipelineBuilder BuildPipeline()
        {
            var transporthandlerFactory = new TransportHandlerFactory();
            ClientPipelineBuilder pipelineBuilder = new ClientPipelineBuilder()
                .With((ctx, innerHandler) => new RetryDelegatingHandler(ctx, innerHandler))
                .With((ctx, innerHandler) => new ErrorDelegatingHandler(ctx, innerHandler))
                .With((ctx, innerHandler) => new TransportDelegatingHandler(ctx, innerHandler))
                .With((ctx, innerHandler) => transporthandlerFactory.Create(ctx));

            return pipelineBuilder;
        }

        private T GetDelegateHandler<T>() where T : DefaultDelegatingHandler
        {
            var handler = InnerHandler as DefaultDelegatingHandler;
            bool isFound = false;

            while (!isFound || handler == null)
            {
                if (handler is T)
                {
                    isFound = true;
                }
                else
                {
                    handler = handler.NextHandler as DefaultDelegatingHandler;
                }
            }

            return !isFound ? default : (T)handler;
        }

        internal async Task<MessageAcknowledgement> OnMessageReceivedAsync(Message message)
        {
            if (Logging.IsEnabled)
                Logging.Enter(this, message, nameof(OnMessageReceivedAsync));

            Debug.Assert(message != null, "Received a null message");

            // Grab this semaphore so that there is no chance that the _receiveMessageCallback instance is set in between the read of the
            // item1 and the read of the item2
            await _receiveMessageSemaphore.WaitAsync().ConfigureAwait(false);

            try
            {
                Func<Message, Task<MessageAcknowledgement>> callback = _receiveMessageCallback;

                if (callback != null)
                {
                    return await callback.Invoke(message).ConfigureAwait(false);
                }

                // The SDK should only receive messages when the user sets a listener, so this should never happen.
                if (Logging.IsEnabled)
                    Logging.Error(this, "Received a message when no listener was set. Abandoning message.", nameof(OnMessageReceivedAsync));

                return MessageAcknowledgement.Abandon;
            }
            finally
            {
                if (Logging.IsEnabled)
                    Logging.Exit(this, message, nameof(OnMessageReceivedAsync));

                _receiveMessageSemaphore.Release();
            }
        }

        private Task EnableReceiveMessageAsync(CancellationToken cancellationToken = default)
        {
            // The telemetry downlink needs to be enabled only for the first time that the _receiveMessageCallback callback is set.
            return _receiveMessageCallback == null
                ? InnerHandler.EnableReceiveMessageAsync(cancellationToken)
                : Task.CompletedTask;
        }

        private Task DisableReceiveMessageAsync(CancellationToken cancellationToken = default)
        {
            // The telemetry downlink should be disabled only after _receiveMessageCallback callback has been removed.
            return _receiveMessageCallback == null
                ? InnerHandler.DisableReceiveMessageAsync(cancellationToken)
                : Task.CompletedTask;
        }
    }
}