// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace Microsoft.Azure.Devices.Client
{
    /// <summary>
    /// Authentication method that uses a shared access signature token and allows for token refresh.
    /// </summary>
    public abstract class ModuleAuthenticationWithTokenRefresh : AuthenticationWithTokenRefresh
    {
        /// <summary>
        /// Creates an instance of this class.
        /// </summary>
        /// <param name="deviceId">The device Id.</param>
        /// <param name="moduleId">The module Id.</param>
        /// <param name="suggestedTimeToLive">
        /// The suggested time to live value for the generated SAS tokens.
        /// The default value is 1 hour.
        /// </param>
        /// <param name="timeBufferPercentage">
        /// The time buffer before expiry when the token should be renewed, expressed as a percentage of the time to live.
        /// The default behavior is that the token will be renewed when it has 15% or less of its lifespan left.
        ///</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="deviceId"/> or <paramref name="moduleId"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="deviceId"/> or <paramref name="moduleId"/> is empty or white space.</exception>
        public ModuleAuthenticationWithTokenRefresh(
            string deviceId,
            string moduleId,
            TimeSpan suggestedTimeToLive = default,
            int timeBufferPercentage = default)
            : base(suggestedTimeToLive,
                  timeBufferPercentage)
        {
            Argument.AssertNotNullOrWhiteSpace(deviceId, nameof(deviceId));
            Argument.AssertNotNullOrWhiteSpace(moduleId, nameof(moduleId));

            ModuleId = moduleId;
            DeviceId = deviceId;
        }

        /// <summary>
        /// Gets the module's Id.
        /// </summary>
        public string ModuleId { get; }

        /// <summary>
        /// Gets the device's Id.
        /// </summary>
        public string DeviceId { get; }

        /// <summary>
        /// Populates a supplied instance based on the properties of the current instance.
        /// </summary>
        /// <param name="iotHubConnectionCredentials">Instance to populate.</param>
        /// <returns>A populated class instance.</returns>
        public override IotHubConnectionCredentials Populate(IotHubConnectionCredentials iotHubConnectionCredentials)
        {
            iotHubConnectionCredentials = base.Populate(iotHubConnectionCredentials);
            iotHubConnectionCredentials.DeviceId = DeviceId;
            iotHubConnectionCredentials.ModuleId = ModuleId;
            return iotHubConnectionCredentials;
        }
    }
}
