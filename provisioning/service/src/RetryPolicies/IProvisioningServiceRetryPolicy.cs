﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace Microsoft.Azure.Devices.Provisioning.Service
{
    /// <summary>
    /// Represents a retry policy for the DPS service client.
    /// </summary>
    public interface IProvisioningServiceRetryPolicy
    {
        /// <summary>
        /// Method called by the client when an operation fails to determine if a retry should be attempted,
        /// and how long to wait until retrying the operation.
        /// </summary>
        /// <param name="currentRetryCount">The number of times the current operation has been attempted.</param>
        /// <param name="lastException">The exception that prompted this retry policy check.</param>
        /// <param name="retryDelay">Set this to the desired time to delay before the next attempt.</param>
        /// <returns>True if the operation should be retried; otherwise false.</returns>
        bool ShouldRetry(uint currentRetryCount, Exception lastException, out TimeSpan retryDelay);
    }
}