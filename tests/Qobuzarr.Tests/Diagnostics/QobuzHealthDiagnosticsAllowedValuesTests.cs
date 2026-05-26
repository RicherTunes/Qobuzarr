// <copyright file="QobuzHealthDiagnosticsAllowedValuesTests.cs" company="RicherTunes">
// Copyright (c) RicherTunes. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Common.Abstractions.Diagnostics;
using Lidarr.Plugin.Common.TestKit.Compliance;
using Lidarr.Plugin.Qobuzarr.Diagnostics;
using Xunit;

namespace Qobuzarr.Tests.Diagnostics;

/// <summary>
/// Validates that all DiagnosticHealthResult instances produced by QobuzHealthDiagnostics
/// use only well-known, registered error codes, diagnostic types, and capabilities.
/// </summary>
public class QobuzHealthDiagnosticsAllowedValuesTests : DiagnosticsAllowedValuesTestBase
{
    protected override IReadOnlySet<string> AllowedErrorCodes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        QobuzHealthDiagnostics.ErrorCodes.AuthFailed,
        QobuzHealthDiagnostics.ErrorCodes.ConnectionFailed,
    };

    protected override IReadOnlySet<string> AllowedDiagnosticTypes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        QobuzHealthDiagnostics.DiagnosticTypes.AuthValidate,
        QobuzHealthDiagnostics.DiagnosticTypes.Connectivity,
    };

    protected override IReadOnlySet<string> AllowedCapabilities { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        QobuzHealthDiagnostics.Capabilities.LosslessDownload,
        QobuzHealthDiagnostics.Capabilities.Search,
    };

    protected override async Task<IEnumerable<DiagnosticHealthResult>> GetHealthResultsAsync()
    {
        var results = new List<DiagnosticHealthResult>
        {
            await QobuzHealthDiagnostics.CheckAuthAsync(() => Task.FromResult<(bool, string?)>((true, null))),
            await QobuzHealthDiagnostics.CheckAuthAsync(() => Task.FromResult<(bool, string?)>((false, "test error"))),
            await QobuzHealthDiagnostics.CheckAuthAsync(() => throw new InvalidOperationException("test")),
            QobuzHealthDiagnostics.CheckConnectivity(hasRequests: true),
            QobuzHealthDiagnostics.CheckConnectivity(hasRequests: false),
            QobuzHealthDiagnostics.CheckDownloadPath(pathValid: true),
            QobuzHealthDiagnostics.CheckDownloadPath(pathValid: false),
        };
        return results;
    }

    // Per-scenario facts kept for richer failure output
    [Fact]
    public async Task CheckAuthAsync_Success_UsesOnlyRegisteredValues()
        => AssertAllowed(await QobuzHealthDiagnostics.CheckAuthAsync(() => Task.FromResult<(bool, string?)>((true, null))), "CheckAuthAsync(success)");

    [Fact]
    public async Task CheckAuthAsync_Failure_UsesOnlyRegisteredValues()
        => AssertAllowed(await QobuzHealthDiagnostics.CheckAuthAsync(() => Task.FromResult<(bool, string?)>((false, "test error"))), "CheckAuthAsync(failure)");

    [Fact]
    public async Task CheckAuthAsync_Exception_UsesOnlyRegisteredValues()
        => AssertAllowed(await QobuzHealthDiagnostics.CheckAuthAsync(() => throw new InvalidOperationException("test")), "CheckAuthAsync(exception)");

    [Fact]
    public void CheckConnectivity_HasRequests_UsesOnlyRegisteredValues()
        => AssertAllowed(QobuzHealthDiagnostics.CheckConnectivity(hasRequests: true), "CheckConnectivity(hasRequests=true)");

    [Fact]
    public void CheckConnectivity_NoRequests_UsesOnlyRegisteredValues()
        => AssertAllowed(QobuzHealthDiagnostics.CheckConnectivity(hasRequests: false), "CheckConnectivity(hasRequests=false)");

    [Fact]
    public void CheckDownloadPath_Valid_UsesOnlyRegisteredValues()
        => AssertAllowed(QobuzHealthDiagnostics.CheckDownloadPath(pathValid: true), "CheckDownloadPath(valid)");

    [Fact]
    public void CheckDownloadPath_Invalid_UsesOnlyRegisteredValues()
        => AssertAllowed(QobuzHealthDiagnostics.CheckDownloadPath(pathValid: false), "CheckDownloadPath(invalid)");

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
