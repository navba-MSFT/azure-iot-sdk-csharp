﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
using CommandLine;
using System;
using System.Collections.Generic;

namespace Microsoft.Azure.Devices.Client.Samples
{
    public enum Transport
    {
        Mqtt,
        Amqp,
    };

    /// <summary>
    /// Parameters for the application.
    /// </summary>
    internal class Parameters
    {
        [Option(
            'c',
            "PrimaryConnectionString",
            Required = false,
            HelpText = "The primary connection string for the device to simulate.")]
        public string PrimaryConnectionString { get; set; }

        [Option(
            "SecondaryConnectionString",
            Required = false,
            HelpText = "The secondary connection string for the device to simulate.")]
        public string SecondaryConnectionString { get; set; }

        [Option(
            't',
            "Transport",
            Default = Transport.Amqp,
            Required = false,
            HelpText = "The transport to use for the connection.")]
        public Transport Transport { get; set; }

        [Option(
           "TransportProtocol",
           Default = IotHubClientTransportProtocol.Tcp,
           HelpText = "The transport to use to communicate with the device provisioning instance.")]
        public IotHubClientTransportProtocol TransportProtocol { get; set; }

        [Option(
            'n',
            "CertificateName",
            Default = "certificate.pfx",
            Required = false,
            HelpText = "The PFX certificate to load for authentication.")]
        public string CertificateName { get; set; }

        [Option(
            'p',
            "CertificatePassword",
            Required = false,
            HelpText = "The password of the PFX certificate file.")]
        public string CertificatePassword { get; set; }

        [Option(
            'd',
            "DeviceId",
            Required = false,
            HelpText = "The Id of device.")]
        public string DeviceId { get; set; }

        [Option(
            'h',
            "HostName",
            Required = false,
            HelpText = "The hostname of IoT hub.")]
        public string HostName { get; set; }

        [Option(
            'r',
            "Application running time (in seconds)",
            Required = false,
            HelpText = "The running time for this console application. Leave it unassigned to run the application until it is explicitly canceled using Control+C.")]
        public double? ApplicationRunningTime { get; set; }

        internal List<string> GetConnectionStrings()
        {
            var cs = new List<string>(2)
            {
                PrimaryConnectionString,
            };

            if (!string.IsNullOrWhiteSpace(SecondaryConnectionString))
            {
                cs.Add(SecondaryConnectionString);
            }

            return cs;
        }

        internal IotHubClientTransportSettings GetHubTransportSettings()
        {
            return Transport switch
            {
                Transport.Mqtt => new IotHubClientMqttSettings(TransportProtocol),
                Transport.Amqp => new IotHubClientAmqpSettings(TransportProtocol),
                _ => throw new NotSupportedException($"Unsupported transport type {Transport}/{TransportProtocol}"),
            };
        }
    }
}
