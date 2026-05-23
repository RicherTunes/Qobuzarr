using System.Linq;
using System.Reflection;

using FluentAssertions;

using Lidarr.Plugin.Qobuzarr.API;
using Lidarr.Plugin.Qobuzarr.Authentication;
using Lidarr.Plugin.Qobuzarr.Integration;

using Xunit;

namespace Qobuzarr.Tests.API;

/// <summary>
/// Regression guards: every log call site that interpolates response bodies
/// or app-secret derivation inputs must route the sensitive arg through
/// <c>LogRedactor.Redact</c>. The static check is a coarse net — it scans
/// the compiled IL for known-leak call patterns — but it catches the most
/// common regression class: a future contributor removes the Redact wrapper
/// and the leak comes back.
/// </summary>
public sealed class QobuzApiClientLogRedactionTests
{
    [Fact]
    public void QobuzApiClient_Assembly_NoLog_References_ResponseContent_Without_Redactor()
    {
        // Source-level invariant: any string passed to a NLog Logger method
        // that originates from `response.Content` or `bundleContent` must be
        // wrapped through LogRedactor.Redact. The check is grep-shaped (we
        // read the .cs source rather than IL) because string-interpolation
        // is compiler-flattened in IL and hard to inspect that way.
        var sourcePath = ResolveSourcePath("API", "QobuzApiClient.cs");
        var lines = System.IO.File.ReadAllLines(sourcePath);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            // Logger call that interpolates response.Content directly without redaction.
            if ((line.Contains("_logger.") || line.Contains("Logger.")) &&
                line.Contains("response.Content") &&
                !line.Contains("LogRedactor.Redact") &&
                !line.Contains("response.Content?.Length") &&    // length-only is safe
                !line.Contains("response.Content.Length"))
            {
                Assert.Fail($"QobuzApiClient.cs:{i + 1} logs response.Content without LogRedactor.Redact wrapping. Line: {line.Trim()}");
            }
        }
    }

    [Fact]
    public void QobuzAuthenticationService_AppSecret_Derivation_Inputs_Are_Redacted()
    {
        // seed + info + extras concatenated form the live app-secret. If any
        // of them is logged in clear, the signing key is exposed.
        var sourcePath = ResolveSourcePath("Authentication", "QobuzAuthenticationService.cs");
        var lines = System.IO.File.ReadAllLines(sourcePath);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var isLogCall = line.Contains("_logger.") || line.Contains("Logger.");
            if (!isLogCall) continue;

            // Interpolation references the derivation variables in clear?
            var interpolatesSensitive =
                line.Contains("{seed}") || line.Contains("{info}") || line.Contains("{extras}");
            if (interpolatesSensitive && !line.Contains("LogRedactor.Redact"))
            {
                Assert.Fail($"QobuzAuthenticationService.cs:{i + 1} logs app-secret derivation input without LogRedactor.Redact. Line: {line.Trim()}");
            }
        }
    }

    [Fact]
    public void TokenRefresher_Logs_Redact_Exception_Message()
    {
        var sourcePath = ResolveSourcePath("Authentication", "TokenRefresher.cs");
        var lines = System.IO.File.ReadAllLines(sourcePath);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if ((line.Contains("_logger.") || line.Contains("Logger.")) &&
                System.Text.RegularExpressions.Regex.IsMatch(line, @"\bex\.Message\b") &&
                !line.Contains("LogRedactor.Redact"))
            {
                Assert.Fail($"TokenRefresher.cs:{i + 1} logs ex.Message without LogRedactor.Redact. Line: {line.Trim()}");
            }
        }
    }

    [Fact]
    public void AuthTokenManager_Logs_Redact_Exception_Message()
    {
        var sourcePath = ResolveSourcePath("Services", "AuthTokenManager.cs");
        var lines = System.IO.File.ReadAllLines(sourcePath);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var isLogCall = line.Contains("_logger.") || line.Contains("Logger.");
            if (!isLogCall) continue;

            // Match any `<identifier>.Message` interpolation (authEx.Message, ex.Message, etc.)
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"\b\w+\.Message\b") &&
                !line.Contains("LogRedactor.Redact"))
            {
                Assert.Fail($"AuthTokenManager.cs:{i + 1} logs exception.Message without LogRedactor.Redact. Line: {line.Trim()}");
            }
        }
    }

    [Fact]
    public void LidarrApiClient_Deserialize_Failure_Path_Redacts_ResponseContent()
    {
        var sourcePath = ResolveSourcePath("Integration", "LidarrApiClient.cs");
        var lines = System.IO.File.ReadAllLines(sourcePath);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if ((line.Contains("_logger.") || line.Contains("Logger.")) &&
                line.Contains("response.Content") &&
                !line.Contains("LogRedactor.Redact"))
            {
                Assert.Fail($"LidarrApiClient.cs:{i + 1} logs response.Content without LogRedactor.Redact. Line: {line.Trim()}");
            }
        }
    }

    private static string ResolveSourcePath(params string[] subPath)
    {
        // Walk up until we find the qobuzarr repo root (recognized by the
        // top-level Qobuzarr.csproj file). Relying on `src/` alone is
        // ambiguous because nested test directories can contain a `src/`
        // shadow under their bin/ output.
        var dir = new System.IO.DirectoryInfo(typeof(QobuzApiClientLogRedactionTests).Assembly.Location).Parent;
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "Qobuzarr.csproj")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull("test must run inside the qobuzarr repo tree");
        var full = System.IO.Path.Combine(new[] { dir!.FullName, "src" }.Concat(subPath).ToArray());
        System.IO.File.Exists(full).Should().BeTrue($"expected source at {full}");
        return full;
    }
}
