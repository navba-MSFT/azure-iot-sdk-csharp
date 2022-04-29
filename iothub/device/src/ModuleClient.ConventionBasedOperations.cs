﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Shared;

namespace Microsoft.Azure.Devices.Client
{
    /// <summary>
    /// Contains methods that a convention based module can use to send telemetry to the service,
    /// respond to commands and perform operations on its properties.
    /// </summary>
    /// <threadsafety static="true" instance="true" />
    public partial class ModuleClient : IDisposable
#if !NET451 && !NET472 && !NETSTANDARD2_0
        , IAsyncDisposable
#endif
    {
        /// <summary>
        /// The <see cref="PayloadConvention"/> that the client uses for convention-based operations.
        /// </summary>
        public PayloadConvention PayloadConvention => InternalClient.PayloadConvention;

        /// <summary>
        /// Send telemetry using the specified message.
        /// </summary>
        /// <remarks>
        /// Use the <see cref="TelemetryMessage(string)"/> constructor to pass in the optional component name
        /// that the telemetry message is from.
        /// </remarks>
        /// <param name="telemetryMessage">The telemetry message.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        public Task SendTelemetryAsync(TelemetryMessage telemetryMessage, CancellationToken cancellationToken = default)
            => InternalClient.SendTelemetryAsync(telemetryMessage, cancellationToken);

        /// <summary>
        /// Sets the listener for command calls from the service.
        /// </summary>
        /// <param name="callback">The callback to handle all incoming commands for the client.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <example>
        /// Inline:
        /// <code language="csharp">
        /// await client.SubscribeToCommandsAsync(
        ///     (commandRequest) =>
        ///     {
        ///         switch (commandRequest.CommandName)
        ///         {
        ///             case "sampleCommandName":
        ///                 int samplePayload = commandRequest.GetPayload&lt;int&gt;();
        ///                 // process command
        ///                 return Task.FromResult(new CommandRequest(200, relevantPayload));
        ///         }
        ///
        ///         return Task.FromResult(new CommandRequest(CommonClientResponseCodes.BadRequest));
        ///     },
        ///     cancellationToken);
        /// </code>
        /// 
        /// Or as a separate method:
        /// <code language="csharp">
        /// Task&lt;CommandResponse&gt; OnCommandReceived(CommandRequest commandRequest)
        /// {
        ///     // Identify and process supported commands
        /// }
        /// await client.SubscribeToCommandsAsync(OnCommandReceived);
        /// </code>
        /// </example>
        public Task SubscribeToCommandsAsync(Func<CommandRequest, Task<CommandResponse>> callback, CancellationToken cancellationToken = default)
            => InternalClient.SubscribeToCommandsAsync(callback, cancellationToken);

        /// <summary>
        /// Retrieve the client properties.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>The device properties.</returns>
        public Task<ClientProperties> GetClientPropertiesAsync(CancellationToken cancellationToken = default)
            => InternalClient.GetClientTwinPropertiesAsync(cancellationToken);

        /// <summary>
        /// Update the client properties.
        /// This operation enables the partial update of the properties of the connected client.
        /// </summary>
        /// <param name="propertyCollection">Reported properties to push.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <returns>The response of the update operation.</returns>
        public Task<ClientPropertiesUpdateResponse> UpdateClientPropertiesAsync(ClientPropertyCollection propertyCollection, CancellationToken cancellationToken = default)
            => InternalClient.UpdateClientPropertiesAsync(propertyCollection, cancellationToken);

        /// <summary>
        /// Sets the listener for writable property update events.
        /// </summary>
        /// <param name="callback">The callback to handle all writable property updates for the client.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
        /// <example>
        /// Inline:
        /// <code language="csharp">
        /// await client.SubscribeToWritablePropertyUpdateRequestsAsync(
        /// 	async (writableProperties) =>
        /// 	{
        /// 	    var propertiesToBeUpdated = new ClientPropertyCollection();
        /// 		if (writableProperties.TryGetValue("samplePropertyName", out WritableClientProperty propertyUpdateRequested))
        /// 		{
        /// 			propertiesToBeUpdated.AddRootProperty(
        /// 				"samplePropertyName",
        /// 				propertyUpdateRequested.AcknowledgeWith(CommonClientResponseCodes.OK, "The operation completed successfully."));
        /// 		}
        /// 		ClientPropertiesUpdateResponse updateResponse = await client.UpdateClientPropertiesAsync(propertiesToBeUpdated, cancellationToken);
        /// 	},
        /// 	cancellationToken);
        /// </code>
        /// 
        /// Or as a separate method:
        /// <code language="csharp">
        /// async Task OnPropertyUpdateRequestReceivedAsync(ClientPropertyCollection writableProperties)
        /// {
        ///     // Identify and process supported writable property update requests
        /// }
        /// await client.SubscribeToWritablePropertyUpdateRequestsAsync(OnPropertyUpdateRequestReceivedAsync, cancellationToken);
        /// </code>
        /// </example>
        public Task SubscribeToWritablePropertyUpdateRequestsAsync(Func<ClientPropertyCollection, Task> callback, CancellationToken cancellationToken = default)
            => InternalClient.SubscribeToWritablePropertyUpdateRequestsAsync(callback, cancellationToken);
    }
}