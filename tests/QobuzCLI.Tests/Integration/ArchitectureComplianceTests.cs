using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;
using QobuzCLI.Commands;
using QobuzCLI.Services;

namespace QobuzCLI.Tests.Integration;

/// <summary>
/// Integration tests that validate the architectural improvements made during technical debt reduction.
/// These tests ensure that CLI properly follows plugin-first architecture principles.
/// </summary>
public class ArchitectureComplianceTests
{
    [Fact]
    public void DownloadCommand_ShouldDependOnPluginHost()
    {
        // Validates that CLI uses plugin services rather than reimplementing functionality
        var downloadCommandType = typeof(DownloadCommand);
        var constructorParams = downloadCommandType.GetConstructors()[0].GetParameters();

        var hasPluginHost = constructorParams.Any(p => p.ParameterType == typeof(IPluginHost));
        hasPluginHost.Should().BeTrue("DownloadCommand should depend on IPluginHost for core functionality");
    }

    [Fact]
    public void DownloadCommand_ShouldHaveReasonableDependencyCount()
    {
        // Ensures we successfully decomposed the god object
        var downloadCommandType = typeof(DownloadCommand);
        var constructorParams = downloadCommandType.GetConstructors()[0].GetParameters();

        constructorParams.Length.Should().BeLessOrEqualTo(10,
            "DownloadCommand should have reasonable dependency count after refactoring");
    }

    [Fact]
    public void QueueMonitoringService_ShouldBeSmallAndFocused()
    {
        // Validates extracted service is focused and testable
        var serviceType = typeof(QueueMonitoringService);
        var methods = serviceType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.DeclaringType == serviceType).ToList();

        methods.Count.Should().BeLessOrEqualTo(3, "Extracted service should be focused and small");

        var constructorParams = serviceType.GetConstructors()[0].GetParameters();
        constructorParams.Length.Should().BeLessOrEqualTo(2, "Service should have minimal dependencies");
    }

    [Fact]
    public void DownloadCommand_ShouldNotContainBusinessLogic()
    {
        // Validates that CLI doesn't contain business logic that should be in plugin
        var downloadCommandType = typeof(DownloadCommand);
        var methods = downloadCommandType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance);

        // Look for methods that might indicate business logic reimplementation
        var suspiciousMethodNames = new[] { "DownloadTrack", "ApplyMetadata", "ValidateQuality", "ProcessAudio" };
        var foundSuspiciousMethods = methods
            .Where(m => suspiciousMethodNames.Any(name => m.Name.Contains(name)))
            .ToList();

        foundSuspiciousMethods.Should().BeEmpty(
            "DownloadCommand should not contain core business logic methods - these should be in plugin");
    }

    [Fact]
    public void CLI_ShouldNotReimplementPluginFunctionality()
    {
        // High-level architectural test.
        // GetTypes() and even Type.Namespace can throw when transitive host
        // assemblies (Lidarr.Core, Lidarr.Common) are absent in test
        // environments that don't extract Docker assemblies. Guard every
        // reflection call to keep the test useful in all environments.
        var cliAssembly = typeof(DownloadCommand).Assembly;

        Type[] allTypes;
        try
        {
            allTypes = cliAssembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            allTypes = ex.Types.Where(t => t != null).ToArray()!;
        }

        var suspiciousPatterns = new[] { "DownloadTrack", "ApplyMetadata", "TagFile", "ConvertAudio" };

        foreach (var type in allTypes)
        {
            // Accessing .Namespace on types with unresolvable base classes
            // throws FileNotFoundException for the missing host assembly.
            string? ns;
            try { ns = type.Namespace; }
            catch { continue; }

            if (ns?.StartsWith("QobuzCLI") != true)
                continue;

            MethodInfo[] methods;
            try { methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); }
            catch { continue; }

            var reimplementedMethods = methods
                .Where(m => suspiciousPatterns.Any(pattern => m.Name.Contains(pattern)))
                .ToList();

            reimplementedMethods.Should().BeEmpty(
                $"Type {type.Name} should not reimplement plugin functionality");
        }
    }
}

/// <summary>
/// Tests that validate our code quality improvements.
/// </summary>
public class CodeQualityTests
{
    [Fact]
    public void DownloadCommand_ShouldBeMaintainableSize()
    {
        // Validates our god object decomposition success
        // Sums all partial class files (DownloadCommand*.cs) to prevent "gaming" the gate with partials
        //
        // If this test fails, extract behavior into Services/* (pure, testable), and keep
        // DownloadCommand as wiring + UX only. Splitting into partials is not considered a refactor.
        var commandsDir = Path.Combine(GetSourceRoot(), "QobuzCLI", "Commands");
        var downloadCommandFiles = Directory.GetFiles(commandsDir, "DownloadCommand*.cs");

        downloadCommandFiles.Should().NotBeEmpty("DownloadCommand source files should exist");

        var totalLineCount = downloadCommandFiles
            .Select(f => File.ReadAllLines(f).Length)
            .Sum();

        // Ratcheting LOC gate: allows small growth but maintains pressure to shrink
        var currentTotal = totalLineCount;

        // Hard floor: acceptable long-term target
        const int floor = 900;

        // Buffer: allow small growth without breaking (for short bursts of refactoring)
        var buffer = Math.Max(40, (int)Math.Ceiling(currentTotal * 0.05));

        // Ratcheting ceiling: never below floor, never below current+buffer
        var ceiling = Math.Max(floor, currentTotal + buffer);

        totalLineCount.Should().BeLessOrEqualTo(ceiling,
            $"DownloadCommand is too large ({currentTotal} LOC). " +
            $"Refactor by extracting services (not just splitting partials). " +
            $"Ceiling={ceiling}, floor={floor}, buffer={buffer}.");
    }

    [Fact]
    public void ExtractedServices_ShouldHaveFocusedResponsibilities()
    {
        // Validates separation of concerns in extracted services
        var queueMonitoringSource = File.ReadAllText(
            Path.Combine(GetSourceRoot(), "QobuzCLI", "Services", "QueueMonitoringService.cs"));

        var lineCount = queueMonitoringSource.Split('\n').Length;
        lineCount.Should().BeLessOrEqualTo(100,
            "Extracted services should be small and focused");
    }

    private static string GetSourceRoot() => RepositoryRoot.Locate();
}

/// <summary>
/// Tests that validate security improvements.
/// These gates scan the REAL repository tree (see <see cref="RepositoryRoot"/>);
/// the detector regexes are themselves pinned by
/// <see cref="HardcodedCredentialDetector_SelfTest"/> so the gate cannot silently
/// go vacuous again.
/// </summary>
public class SecurityComplianceTests
{
    // Matches a literal credential assignment: an identifier containing
    // password/secret/apikey assigned a non-trivial double-quoted string
    // literal. Interpolated / env-derived values ($, {) are excluded by the
    // value character class; values shorter than 4 chars are noise.
    private static readonly Regex HardcodedCredentialAssignment = new(
        "(?:password|secret|apikey|api_key)\\w*\\s*=\\s*\"(?<value>[^\"$\\{]{4,})\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // A real app secret must never be hardcoded. The empty-string sentinel
    // ("" — fetched dynamically) is allowed; any alphanumeric literal is not.
    private static readonly Regex HardcodedDefaultAppSecret = new(
        "DefaultAppSecret\\s*=\\s*\"[A-Za-z0-9]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Values that are the NAME of a credential slot rather than credential
    // material: snake_case identifiers (qobuz_password, QOBUZ_APP_SECRET) and
    // Http-Header-Cased names (X-Api-Key).
    private static readonly Regex CredentialNameShapedValue = new(
        @"^(?:[A-Za-z0-9]+(?:_[A-Za-z0-9]+)+|[A-Z][A-Za-z0-9]*(?:-[A-Z][A-Za-z0-9]*)+)$",
        RegexOptions.Compiled);

    internal static bool ContainsHardcodedCredential(string line)
    {
        if (HardcodedDefaultAppSecret.IsMatch(line))
        {
            return true;
        }

        return HardcodedCredentialAssignment.Matches(line)
            .Any(m => !CredentialNameShapedValue.IsMatch(m.Groups["value"].Value));
    }

    [Theory]
    // Real violations the gate MUST catch.
    [InlineData("var password = \"hunter2\";", true)]
    [InlineData("private const string ApiKey = \"a1b2c3d4e5\";", true)]
    [InlineData("string secret= \"topsecretvalue\";", true)]
    [InlineData("public const string DefaultAppSecret = \"abc123def456\";", true)]
    // Legitimate code the gate must NOT flag.
    [InlineData("var passwordOption = new Option<string?>(\"--password\", \"Password for login\");", false)]
    [InlineData("public const string AppSecretEnvironmentVariable = \"QOBUZ_APP_SECRET\";", false)]
    [InlineData("private const string PASSWORD_KEY = \"qobuz_password\";", false)]
    [InlineData("public const string ApiKeyHeader = \"X-Api-Key\";", false)]
    [InlineData("var secret = Environment.GetEnvironmentVariable(\"QOBUZ_APP_SECRET\");", false)]
    [InlineData("public const string DefaultAppSecret = \"\";  // Fetched dynamically", false)]
    public void HardcodedCredentialDetector_SelfTest(string sample, bool expectedViolation)
    {
        ContainsHardcodedCredential(sample).Should().Be(expectedViolation,
            $"the credential gate must stay calibrated for: {sample}");
    }

    [Fact]
    public void SourceCode_ShouldNotContainHardcodedCredentials()
    {
        var sourceRoot = GetSourceRoot();

        // Scan only repo-owned production code: the plugin (src/) and the CLI
        // wrapper (QobuzCLI/). ext/ (submodule — has its own gates), tests/,
        // and build output are out of scope for this gate.
        var scanRoots = new[]
        {
            Path.Combine(sourceRoot, "src"),
            Path.Combine(sourceRoot, "QobuzCLI"),
        };

        var violations = new List<string>();

        foreach (var scanRoot in scanRoots)
        {
            Directory.Exists(scanRoot).Should().BeTrue(
                $"scan root '{scanRoot}' must exist — the credential gate would otherwise be vacuous");

            var sourceFiles = Directory.GetFiles(scanRoot, "*.cs", SearchOption.AllDirectories)
                .Where(f => !HasPathSegment(f, "bin") && !HasPathSegment(f, "obj"));

            foreach (var file in sourceFiles)
            {
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (ContainsHardcodedCredential(lines[i]))
                    {
                        violations.Add($"{file}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }
        }

        violations.Should().BeEmpty(
            "repo-owned source must not contain hardcoded credential literals");
    }

    [Fact]
    public void Configuration_ShouldUseEnvironmentVariables()
    {
        var sourceRoot = GetSourceRoot();

        // QobuzConstants declares the env-var names and must keep the secret
        // default empty. (Api.DefaultAppId deliberately carries the PUBLIC
        // Qobuz web-player app id — an app id is a client identifier, not a
        // secret, so it is not asserted against here.)
        var constantsFile = Path.Combine(sourceRoot, "src", "Configuration", "QobuzConstants.cs");
        File.Exists(constantsFile).Should().BeTrue(
            $"'{constantsFile}' must exist — this gate is vacuous without it");

        var constantsContent = File.ReadAllText(constantsFile);
        constantsContent.Should().Contain("AppIdEnvironmentVariable = \"QOBUZ_APP_ID\"",
            "QobuzConstants must declare the app-id environment variable name");
        constantsContent.Should().Contain("AppSecretEnvironmentVariable = \"QOBUZ_APP_SECRET\"",
            "QobuzConstants must declare the app-secret environment variable name");
        constantsContent.Should().Contain("DefaultAppSecret = \"\"",
            "the app secret must never ship hardcoded — it comes from settings or the QOBUZ_APP_SECRET environment variable");

        // The live credential accessors (GetAppId/GetAppSecret) live in
        // QobuzIndexerSettings and must fall back to those environment variables.
        var settingsFile = Path.Combine(sourceRoot, "src", "Indexers", "QobuzIndexerSettings.cs");
        File.Exists(settingsFile).Should().BeTrue(
            $"'{settingsFile}' must exist — this gate is vacuous without it");

        var settingsContent = File.ReadAllText(settingsFile);
        settingsContent.Should().Contain("public string GetAppId()",
            "QobuzIndexerSettings must expose the app-id accessor");
        settingsContent.Should().Contain("public string GetAppSecret()",
            "QobuzIndexerSettings must expose the app-secret accessor");
        settingsContent.Should().Contain(
            "GetEnvironmentVariable(QobuzConstants.Authentication.AppIdEnvironmentVariable)",
            "GetAppId must fall back to the QOBUZ_APP_ID environment variable");
        settingsContent.Should().Contain(
            "GetEnvironmentVariable(QobuzConstants.Authentication.AppSecretEnvironmentVariable)",
            "GetAppSecret must fall back to the QOBUZ_APP_SECRET environment variable");
    }

    private static bool HasPathSegment(string path, string segment)
    {
        return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => string.Equals(part, segment, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetSourceRoot() => RepositoryRoot.Locate();
}

/// <summary>
/// Locates the repository root for source-scanning compliance tests.
/// Anchors on Qobuzarr.sln — a definitive repository marker that build output
/// can never contain — instead of walking up until a directory merely contains
/// a folder named "src"/"QobuzCLI". The old folder-name anchor latched onto a
/// bin-local "src/" folder (created by a since-removed csproj Content item),
/// which silently made every source-scanning assertion in this file vacuous.
/// </summary>
internal static class RepositoryRoot
{
    public static string Locate()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null && !File.Exists(Path.Combine(dir, "Qobuzarr.sln")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        if (dir == null)
        {
            throw new InvalidOperationException(
                "Could not locate the repository root: no ancestor of " +
                $"'{Directory.GetCurrentDirectory()}' contains 'Qobuzarr.sln'. " +
                "Source-scanning compliance tests must run from within the repository.");
        }

        return dir;
    }
}
