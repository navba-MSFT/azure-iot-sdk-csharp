﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using Microsoft.Azure.Amqp;
using Microsoft.Azure.Amqp.Encoding;
using Microsoft.Azure.Amqp.Framing;
using Microsoft.Azure.Devices.Common.Client;

namespace Microsoft.Azure.Devices.Common.Exceptions
{
    internal static class AmqpErrorMapper
    {
        public static Exception GetExceptionFromOutcome(Outcome outcome)
        {
            if (outcome == null)
            {
                return new IotHubException("Unknown error.");
            }

            Exception retException;
            if (outcome.DescriptorCode == Rejected.Code)
            {
                var rejected = (Rejected)outcome;
                retException = ToIotHubClientContract(rejected.Error);
            }
            else if (outcome.DescriptorCode == Released.Code)
            {
                retException = new OperationCanceledException("AMQP link released.");
            }
            else
            {
                retException = new IotHubException("Unknown error.");
            }

            return retException;
        }

        public static Exception ToIotHubClientContract(Error error)
        {
            if (error == null)
            {
                return new IotHubException("Unknown error.");
            }

            Exception retException;
            string message = error.Description;
            string trackingId = null;

            if (error.Info != null
                && error.Info.TryGetValue(IotHubAmqpProperty.TrackingId, out trackingId))
            {
                message = $"{message}\r\nTracking Id:{trackingId}";
            }

            if (error.Condition.Equals(IotHubAmqpErrorCode.TimeoutError))
            {
                retException = new TimeoutException(message);
            }
            else if (error.Condition.Equals(AmqpErrorCode.NotFound))
            {
                retException = new DeviceNotFoundException(message, innerException: null);
            }
            else if (error.Condition.Equals(AmqpErrorCode.NotImplemented))
            {
                retException = new NotSupportedException(message);
            }
            else if (error.Condition.Equals(IotHubAmqpErrorCode.MessageLockLostError))
            {
                retException = new DeviceMessageLockLostException(message);
            }
            else if (error.Condition.Equals(AmqpErrorCode.NotAllowed))
            {
                retException = new InvalidOperationException(message);
            }
            else if (error.Condition.Equals(AmqpErrorCode.UnauthorizedAccess))
            {
                retException = new UnauthorizedException(message);
            }
            else if (error.Condition.Equals(IotHubAmqpErrorCode.ArgumentError))
            {
                retException = new ArgumentException(message);
            }
            else if (error.Condition.Equals(IotHubAmqpErrorCode.ArgumentOutOfRangeError))
            {
                retException = new ArgumentOutOfRangeException(message);
            }
            else if (error.Condition.Equals(AmqpErrorCode.MessageSizeExceeded))
            {
                retException = new MessageTooLargeException(message);
            }
            else if (error.Condition.Equals(AmqpErrorCode.ResourceLimitExceeded))
            {
                retException = new DeviceMaximumQueueDepthExceededException(message);
            }
            else if (error.Condition.Equals(IotHubAmqpErrorCode.DeviceAlreadyExists))
            {
                retException = new DeviceAlreadyExistsException(message, (Exception)null);
            }
            else if (error.Condition.Equals(IotHubAmqpErrorCode.DeviceContainerThrottled))
            {
                retException = new IotHubThrottledException(message, (Exception)null);
            }
            else if (error.Condition.Equals(IotHubAmqpErrorCode.QuotaExceeded))
            {
                retException = new QuotaExceededException(message, (Exception)null);
            }
            else if (error.Condition.Equals(IotHubAmqpErrorCode.PreconditionFailed))
            {
                retException = new PreconditionFailedException(message, (Exception)null);
            }
            else if (error.Condition.Equals(IotHubAmqpErrorCode.IotHubSuspended))
            {
                retException = new IotHubSuspendedException(message);
            }
            else
            {
                retException = new IotHubException(message);
            }

            if (trackingId != null
                && retException is IotHubException exHub)
            {
                IotHubException iotHubException = exHub;
                iotHubException.TrackingId = trackingId;
                // This is created but not assigned to `retException`. If we change that now, it might be a
                // breaking change. If not for v1, consider for #v2.
            }

            return retException;
        }

        public static ErrorContext GetErrorContextFromException(AmqpException exception)
        {
            Error error = exception.Error;
            AmqpSymbol amqpSymbol = error.Condition;
            string message = error.ToString();
            if (Equals(AmqpErrorCode.ConnectionForced, amqpSymbol)
                || Equals(AmqpErrorCode.ConnectionRedirect, amqpSymbol)
                || Equals(AmqpErrorCode.LinkRedirect, amqpSymbol)
                || Equals(AmqpErrorCode.WindowViolation, amqpSymbol)
                || Equals(AmqpErrorCode.ErrantLink, amqpSymbol)
                || Equals(AmqpErrorCode.HandleInUse, amqpSymbol)
                || Equals(AmqpErrorCode.UnattachedHandle, amqpSymbol)
                || Equals(AmqpErrorCode.DetachForced, amqpSymbol)
                || Equals(AmqpErrorCode.TransferLimitExceeded, amqpSymbol)
                || Equals(AmqpErrorCode.MessageSizeExceeded, amqpSymbol)
                || Equals(AmqpErrorCode.LinkRedirect, amqpSymbol)
                || Equals(AmqpErrorCode.Stolen, amqpSymbol))
            {
                return new ErrorContext(new IotHubException(message, exception));
            }

            return new ErrorContext(new IOException(message, exception));
        }
    }
}