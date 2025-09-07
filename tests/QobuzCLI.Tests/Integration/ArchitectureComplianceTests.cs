using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
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
        // High-level architectural test
        var cliAssembly = typeof(DownloadCommand).Assembly;
        var cliTypes = cliAssembly.GetTypes().Where(t => t.Namespace?.StartsWith("QobuzCLI") == true);

        foreach (var type in cliTypes)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var suspiciousPatterns = new[] { "DownloadTrack", "ApplyMetadata", "TagFile", "ConvertAudio" };
            
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
        var downloadCommandSource = File.ReadAllText(
            Path.Combine(GetSourceRoot(), "QobuzCLI", "Commands", "DownloadCommand.cs"));
        
        var lineCount = downloadCommandSource.Split('\n').Length;
        lineCount.Should().BeLessOrEqualTo(800,
            "DownloadCommand should be maintainable size after refactoring");
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

    private string GetSourceRoot()
    {
        var currentDir = Directory.GetCurrentDirectory();
        // Navigate up to find the source root
        while (!Directory.Exists(Path.Combine(currentDir, "QobuzCLI")) && 
               Directory.GetParent(currentDir) != null)
        {
            currentDir = Directory.GetParent(currentDir)!.FullName;
        }
        return currentDir;
    }
}

/// <summary>
/// Tests that validate security improvements.
/// </summary>
public class SecurityComplianceTests
{
    [Fact]
    public void SourceCode_ShouldNotContainHardcodedCredentials()
    {
        // Validates our security improvements
        var sourceRoot = GetSourceRoot();
        var sourceFiles = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("bin") && !f.Contains("obj") && !f.Contains("Tests"));

        foreach (var file in sourceFiles)
        {
            var content = File.ReadAllText(file);
            
            // Look for patterns that might indicate hardcoded credentials
            var suspiciousPatterns = new[]
            {
                "DefaultAppId.*=.*\"[0-9]",
                "DefaultAppSecret.*=.*\"[a-zA-Z0-9]",
                "password.*=.*\"[^$]", // Exclude environment variables
                "secret.*=.*\"[^$]"
            };

            foreach (var pattern in suspiciousPatterns)
            {
                var hasPattern = System.Text.RegularExpressions.Regex.IsMatch(
                    content, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                hasPattern.Should().BeFalse(
                    $"File {Path.GetFileName(file)} should not contain hardcoded credentials (pattern: {pattern})");
            }
        }
    }

    [Fact]
    public void Configuration_ShouldUseEnvironmentVariables()
    {
        // Validates our secure credential management
        var constantsFile = Path.Combine(GetSourceRoot(), "src", "Configuration", "QobuzConstants.cs");
        if (File.Exists(constantsFile))
        {
            var content = File.ReadAllText(constantsFile);
            
            content.Should().Contain("GetAppId()", 
                "QobuzConstants should use environment variable methods");
            content.Should().Contain("GetAppSecret()", 
                "QobuzConstants should use environment variable methods");
            content.Should().NotContain("DefaultAppId = \"", 
                "QobuzConstants should not contain hardcoded app ID");
        }
    }

    private string GetSourceRoot()
    {
        var currentDir = Directory.GetCurrentDirectory();
        while (!Directory.Exists(Path.Combine(currentDir, "src")) && 
               Directory.GetParent(currentDir) != null)
        {
            currentDir = Directory.GetParent(currentDir)!.FullName;
        }
        return currentDir;
    }
}
