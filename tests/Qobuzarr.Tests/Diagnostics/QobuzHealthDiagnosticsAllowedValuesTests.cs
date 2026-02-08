// <copyright file="QobuzHealthDiagnosticsAllowedValuesTests.cs" company="RicherTunes">
// Copyright (c) RicherTunes. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Common.Abstractions.Diagnostics;
using Lidarr.Plugin.Qobuzarr.Diagnostics;
using Xunit;

namespace Qobuzarr.Tests.Diagnostics;

/// <summary>
/// Validates that all DiagnosticHealthResult instances produced by QobuzHealthDiagnostics
/// use only well-known, registered error codes, diagnostic types, and capabilities.
/// Prevents "stringly-typed" drift over time.
/// </summary>
public class QobuzHealthDiagnosticsAllowedValuesTests
{
    private static readonly HashSet<string> AllowedErrorCodes = new(StringComparer.Ordinal)
    {
        QobuzHealthDiagnostics.ErrorCodes.AuthFailed,
        QobuzHealthDiagnostics.ErrorCodes.ConnectionFailed,
    };

    private static readonly HashSet<string> AllowedDiagnosticTypes = new(StringComparer.Ordinal)
    {
        QobuzHealthDiagnostics.DiagnosticTypes.AuthValidate,
        QobuzHealthDiagnostics.DiagnosticTypes.Connectivity,
    };

    private static readonly HashSet<string> AllowedCapabilities = new(StringComparer.Ordinal)
    {
        QobuzHealthDiagnostics.Capabilities.LosslessDownload,
        QobuzHealthDiagnostics.Capabilities.Search,
    };

    private static void AssertAllowedValues(DiagnosticHealthResult result, string context)
    {
        if (result.ErrorCode is not null)
        {
            AllowedErrorCodes.Should().Contain(result.ErrorCode,
                because: $"ErrorCode '{result.ErrorCode}' from {context} must be a registered value");
        }

        if (result.DiagnosticType is not null)
        {
            AllowedDiagnosticTypes.Should().Contain(result.DiagnosticType,
                because: $"DiagnosticType '{result.DiagnosticType}' from {context} must be a registered value");
        }

        if (result.Capability is not null)
        {
            AllowedCapabilities.Should().Contain(result.Capability,
                because: $"Capability '{result.Capability}' from {context} must be a registered value");
        }
    }

    [Fact]
    public async Task CheckAuthAsync_Success_UsesOnlyRegisteredValues()
    {
        var result = await QobuzHealthDiagnostics.CheckAuthAsync(
            () => Task.FromResult<(bool, string?)>((true, null)));

        AssertAllowedValues(result, "CheckAuthAsync(success)");
    }

    [Fact]
    public async Task CheckAuthAsync_Failure_UsesOnlyRegisteredValues()
    {
        var result = await QobuzHealthDiagnostics.CheckAuthAsync(
            () => Task.FromResult<(bool, string?)>((false, "test error")));

        AssertAllowedValues(result, "CheckAuthAsync(failure)");
    }

    [Fact]
    public async Task CheckAuthAsync_Exception_UsesOnlyRegisteredValues()
    {
        var result = await QobuzHealthDiagnostics.CheckAuthAsync(
            () => throw new InvalidOperationException("test"));

        AssertAllowedValues(result, "CheckAuthAsync(exception)");
    }

    [Fact]
    public void CheckConnectivity_HasRequests_UsesOnlyRegisteredValues()
    {
        var result = QobuzHealthDiagnostics.CheckConnectivity(hasRequests: true);

        AssertAllowedValues(result, "CheckConnectivity(hasRequests=true)");
    }

    [Fact]
    public void CheckConnectivity_NoRequests_UsesOnlyRegisteredValues()
    {
        var result = QobuzHealthDiagnostics.CheckConnectivity(hasRequests: false);

        AssertAllowedValues(result, "CheckConnectivity(hasRequests=false)");
    }

    [Fact]
    public void CheckDownloadPath_Valid_UsesOnlyRegisteredValues()
    {
        var result = QobuzHealthDiagnostics.CheckDownloadPath(pathValid: true);

        AssertAllowedValues(result, "CheckDownloadPath(valid)");
    }

    [Fact]
    public void CheckDownloadPath_Invalid_UsesOnlyRegisteredValues()
    {
        var result = QobuzHealthDiagnostics.CheckDownloadPath(pathValid: false);

        AssertAllowedValues(result, "CheckDownloadPath(invalid)");
    }

    [Fact]
    public void ErrorCodes_AreNotEmpty()
    {
        QobuzHealthDiagnostics.ErrorCodes.AuthFailed.Should().NotBeNullOrWhiteSpace();
        QobuzHealthDiagnostics.ErrorCodes.ConnectionFailed.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void DiagnosticTypes_AreNotEmpty()
    {
        QobuzHealthDiagnostics.DiagnosticTypes.AuthValidate.Should().NotBeNullOrWhiteSpace();
        QobuzHealthDiagnostics.DiagnosticTypes.Connectivity.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Capabilities_AreNotEmpty()
    {
        QobuzHealthDiagnostics.Capabilities.LosslessDownload.Should().NotBeNullOrWhiteSpace();
        QobuzHealthDiagnostics.Capabilities.Search.Should().NotBeNullOrWhiteSpace();
    }
}
