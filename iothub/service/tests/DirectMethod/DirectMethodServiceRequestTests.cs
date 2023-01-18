﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.Azure.Devices.Tests.DirectMethod
{
    [TestClass]
    [TestCategory("Unit")]
    public class DirectMethodServiceRequestTests
    {
        [TestMethod]
        public void DirectMethodServiceRequest_Ctor_ThrowsOnNull()
        {
            Action act = () => _ = new DirectMethodServiceRequest(null);
            act.Should().Throw<ArgumentNullException>();
        }

        [TestMethod]
        [DataRow("")]
        [DataRow(" \t\r\n")]
        public void DirectMethodServiceRequest_Ctor_ThrowsOnEmptyOrWhiteSpace(string methodName)
        {
            Action act = () => _ = new DirectMethodServiceRequest(methodName);
            act.Should().Throw<ArgumentException>();
        }

        [TestMethod]
        public void DirectMethodServiceRequest_Ctor_SetsMethodName()
        {
            // arrange
            const string expected = nameof(expected);

            // act
            var dmcr = new DirectMethodServiceRequest(expected);

            // assert
            dmcr.MethodName.Should().Be(expected);
        }
    }
}