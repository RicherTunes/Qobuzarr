// <copyright file="QobuzHealthDiagnosticsTests.cs" company="RicherTunes">
// Copyright (c) RicherTunes. All rights reserved.
// </copyright>

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Common.Abstractions.Diagnostics;
using Lidarr.Plugin.Qobuzarr.Diagnostics;
using Xunit;

namespace Qobuzarr.Tests.Diagnostics;

public class QobuzHealthDiagnosticsTests
{
    // ---- CheckAuthAsync ----

    [Fact]
    public async Task CheckAuthAsync_SuccessfulAuth_ReturnsHealthyResult()
    {
        // Arrange
        Func<Task<(bool, string?)>> testAuth = () => Task.FromResult<(bool, string?)>((true, null));

        // Act
        var result = await QobuzHealthDiagnostics.CheckAuthAsync(testAuth);

        // Assert
        result.IsHealthy.Should().BeTrue();
        result.StatusMessage.Should().BeNull();
        result.Provider.Should().Be("qobuz");
        result.AuthMethod.Should().Be("app-secret");
        result.DiagnosticType.Should().Be("auth_validate");
        result.Capability.Should().Be("lossless_download");
        result.ErrorCode.Should().BeNull();
        result.ResponseTime.Should().NotBeNull();
        result.ResponseTime!.Value.Should().BeGreaterOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public async Task CheckAuthAsync_FailedAuth_ReturnsUnhealthyWithAuthFailed()
    {
        // Arrange
        Func<Task<(bool, string?)>> testAuth =
            () => Task.FromResult<(bool, string?)>((false, "Invalid credentials"));

        // Act
        var result = await QobuzHealthDiagnostics.CheckAuthAsync(testAuth);

        // Assert
        result.IsHealthy.Should().BeFalse();
        result.StatusMessage.Should().Be("Invalid credentials");
        result.Provider.Should().Be("qobuz");
        result.AuthMethod.Should().Be("app-secret");
        result.DiagnosticType.Should().Be("auth_validate");
        result.Capability.Should().Be("lossless_download");
        result.ErrorCode.Should().Be("AUTH_FAILED");
        result.ResponseTime.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckAuthAsync_FailedAuthWithNullError_UsesDefaultMessage()
    {
        // Arrange
        Func<Task<(bool, string?)>> testAuth =
            () => Task.FromResult<(bool, string?)>((false, null));

        // Act
        var result = await QobuzHealthDiagnostics.CheckAuthAsync(testAuth);

        // Assert
        result.IsHealthy.Should().BeFalse();
        result.StatusMessage.Should().Be("Authentication failed");
        result.ErrorCode.Should().Be("AUTH_FAILED");
    }

    [Fact]
    public async Task CheckAuthAsync_ThrowsException_ReturnsUnhealthyWithConnectionFailed()
    {
        // Arrange
        Func<Task<(bool, string?)>> testAuth =
            () => throw new InvalidOperationException("Network timeout");

        // Act
        var result = await QobuzHealthDiagnostics.CheckAuthAsync(testAuth);

        // Assert
        result.IsHealthy.Should().BeFalse();
        result.StatusMessage.Should().Be("Network timeout");
        result.Provider.Should().Be("qobuz");
        result.AuthMethod.Should().Be("app-secret");
        result.DiagnosticType.Should().Be("auth_validate");
        result.ErrorCode.Should().Be("CONNECTION_FAILED");
        result.Capability.Should().BeNull();
        result.ResponseTime.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckAuthAsync_ThrowsOperationCanceled_PropagatesException()
    {
        // Arrange
        Func<Task<(bool, string?)>> testAuth =
            () => throw new OperationCanceledException("Cancelled");

        // Act
        Func<Task> act = () => QobuzHealthDiagnostics.CheckAuthAsync(testAuth);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CheckAuthAsync_MeasuresResponseTime()
    {
        // Arrange
        Func<Task<(bool, string?)>> testAuth = async () =>
        {
            await Task.Delay(50);
            return (true, (string?)null);
        };

        // Act
        var result = await QobuzHealthDiagnostics.CheckAuthAsync(testAuth);

        // Assert
        result.ResponseTime.Should().NotBeNull();
        result.ResponseTime!.Value.TotalMilliseconds.Should().BeGreaterOrEqualTo(30);
    }

    // ---- CheckConnectivity ----

    [Fact]
    public void CheckConnectivity_HasRequests_ReturnsHealthy()
    {
        // Arrange
        var elapsed = TimeSpan.FromMilliseconds(150);

        // Act
        var result = QobuzHealthDiagnostics.CheckConnectivity(hasRequests: true, elapsed: elapsed);

        // Assert
        result.IsHealthy.Should().BeTrue();
        result.StatusMessage.Should().BeNull();
        result.Provider.Should().Be("qobuz");
        result.AuthMethod.Should().Be("app-secret");
        result.DiagnosticType.Should().Be("connectivity");
        result.Capability.Should().Be("search");
        result.ErrorCode.Should().BeNull();
        result.ResponseTime.Should().Be(elapsed);
    }

    [Fact]
    public void CheckConnectivity_NoRequests_ReturnsUnhealthy()
    {
        // Arrange
        var elapsed = TimeSpan.FromMilliseconds(200);

        // Act
        var result = QobuzHealthDiagnostics.CheckConnectivity(hasRequests: false, elapsed: elapsed);

        // Assert
        result.IsHealthy.Should().BeFalse();
        result.StatusMessage.Should().Be("No search requests generated");
        result.Provider.Should().Be("qobuz");
        result.DiagnosticType.Should().Be("connectivity");
        result.Capability.Should().Be("search");
        result.ErrorCode.Should().Be("CONNECTION_FAILED");
        result.ResponseTime.Should().Be(elapsed);
    }

    [Fact]
    public void CheckConnectivity_NoElapsed_ResponseTimeIsNull()
    {
        // Act
        var result = QobuzHealthDiagnostics.CheckConnectivity(hasRequests: true);

        // Assert
        result.IsHealthy.Should().BeTrue();
        result.ResponseTime.Should().BeNull();
    }

    // ---- CheckDownloadPath ----

    [Fact]
    public void CheckDownloadPath_ValidPath_ReturnsHealthy()
    {
        // Act
        var result = QobuzHealthDiagnostics.CheckDownloadPath(pathValid: true);

        // Assert
        result.IsHealthy.Should().BeTrue();
        result.StatusMessage.Should().BeNull();
        result.Provider.Should().Be("qobuz");
        result.DiagnosticType.Should().Be("connectivity");
        result.Capability.Should().Be("lossless_download");
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public void CheckDownloadPath_InvalidPath_ReturnsUnhealthyWithDefaultMessage()
    {
        // Act
        var result = QobuzHealthDiagnostics.CheckDownloadPath(pathValid: false);

        // Assert
        result.IsHealthy.Should().BeFalse();
        result.StatusMessage.Should().Be("Download path not accessible");
        result.Provider.Should().Be("qobuz");
        result.DiagnosticType.Should().Be("connectivity");
        result.Capability.Should().Be("lossless_download");
        result.ErrorCode.Should().Be("CONNECTION_FAILED");
    }

    [Fact]
    public void CheckDownloadPath_InvalidPathWithCustomError_UsesCustomMessage()
    {
        // Act
        var result = QobuzHealthDiagnostics.CheckDownloadPath(
            pathValid: false,
            errorMessage: "Permission denied: /mnt/music");

        // Assert
        result.IsHealthy.Should().BeFalse();
        result.StatusMessage.Should().Be("Permission denied: /mnt/music");
        result.ErrorCode.Should().Be("CONNECTION_FAILED");
    }

    [Fact]
    public void CheckDownloadPath_ValidPath_HasNoAuthMethod()
    {
        // Act
        var result = QobuzHealthDiagnostics.CheckDownloadPath(pathValid: true);

        // Assert - download path check does not involve auth
        result.AuthMethod.Should().BeNull();
    }

    [Fact]
    public void CheckDownloadPath_InvalidPath_HasNoAuthMethod()
    {
        // Act
        var result = QobuzHealthDiagnostics.CheckDownloadPath(pathValid: false);

        // Assert - download path check does not involve auth
        result.AuthMethod.Should().BeNull();
    }

    // ---- Result type verification ----

    [Fact]
    public async Task AllResults_AreOfTypeDiagnosticHealthResult()
    {
        // Verify that all factory methods return DiagnosticHealthResult (not ProviderHealthResult)
        var authResult = await QobuzHealthDiagnostics.CheckAuthAsync(
            () => Task.FromResult<(bool, string?)>((true, null)));
        var connectivityResult = QobuzHealthDiagnostics.CheckConnectivity(true);
        var downloadResult = QobuzHealthDiagnostics.CheckDownloadPath(true);

        authResult.Should().BeOfType<DiagnosticHealthResult>();
        connectivityResult.Should().BeOfType<DiagnosticHealthResult>();
        downloadResult.Should().BeOfType<DiagnosticHealthResult>();
    }
}
