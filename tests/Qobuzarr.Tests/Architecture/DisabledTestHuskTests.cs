using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Qobuzarr.Tests.Architecture
{
    public sealed class DisabledTestHuskTests
    {
        [Fact]
        public void TestSources_DoNotKeepWholeDisabledTestClassesInBlockComments()
        {
            var repoRoot = FindRepositoryRoot();
            var testsRoot = Path.Combine(repoRoot, "tests");
            var disabledTestBlocks = Directory
                .EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                               !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(path => (
                    path: Path.GetRelativePath(repoRoot, path).Replace('\\', '/'),
                    text: File.ReadAllText(path)))
                .SelectMany(file => ExtractBlockComments(file.text)
                    .Where(block => IsDisabledTestClassBlock(file.text, block))
                    .Select(_ => file.path))
                .Distinct()
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            disabledTestBlocks.Should().BeEmpty(
                "a test source file should either compile and run its tests, use explicit xUnit skip metadata, " +
                "or be deleted after the covered service is removed; whole disabled test classes rot silently");
        }

        [Fact]
        public void ActiveDocsAndTests_DoNotReferenceRemovedDisabledTestHusks()
        {
            var repoRoot = FindRepositoryRoot();
            var removedTestNames = new[]
            {
                "AdaptiveQobuzApiClientTests",
                "AdaptiveRateLimiterTests",
                "DefensiveServicesTests",
                "PlaylistLabelServiceExistenceTests",
                "QobuzQualityManagerTests",
                "ServiceIntegrationTests",
            };

            var activeRoots = new[]
            {
                "docs",
                "tests",
                ".claude",
            };

            var offenders = activeRoots
                .Select(root => Path.Combine(repoRoot, root))
                .Where(Directory.Exists)
                .SelectMany(root => Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                .Select(path => Path.GetRelativePath(repoRoot, path).Replace('\\', '/'))
                .Where(IsActiveTextFile)
                .Select(relativePath => (
                    relativePath,
                    text: File.ReadAllText(Path.Combine(repoRoot, relativePath))))
                .SelectMany(file => removedTestNames
                    .Where(testName => file.text.Contains(testName, StringComparison.Ordinal))
                    .Select(testName => $"{file.relativePath}: {testName}"))
                .OrderBy(entry => entry, StringComparer.Ordinal)
                .ToArray();

            offenders.Should().BeEmpty(
                "active documentation and test comments should not direct maintainers toward deleted disabled-test husks");
        }

        private static bool IsDisabledTestClassBlock(string fileText, string blockComment)
        {
            return fileText.Contains("DISABLED:", StringComparison.OrdinalIgnoreCase) &&
                   Regex.IsMatch(blockComment, @"\bclass\s+\w+Tests\b", RegexOptions.CultureInvariant) &&
                   Regex.IsMatch(blockComment, @"\[(Fact|Theory|SkippableFact|SkippableTheory)\b", RegexOptions.CultureInvariant);
        }

        private static string[] ExtractBlockComments(string text)
        {
            return Regex
                .Matches(text, @"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.CultureInvariant)
                .Select(match => match.Value)
                .ToArray();
        }

        private static bool IsActiveTextFile(string relativePath)
        {
            if (relativePath.StartsWith("docs/archive/", StringComparison.Ordinal) ||
                relativePath.StartsWith("docs/archived/", StringComparison.Ordinal))
            {
                return false;
            }

            if (relativePath.Contains("/bin/", StringComparison.Ordinal) ||
                relativePath.Contains("/obj/", StringComparison.Ordinal) ||
                relativePath.Equals("tests/Qobuzarr.Tests/Architecture/DisabledTestHuskTests.cs", StringComparison.Ordinal))
            {
                return false;
            }

            return relativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                   relativePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
        }

        private static string FindRepositoryRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "Qobuzarr.csproj")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new InvalidOperationException("Could not find qobuzarr repository root.");
        }
    }
}
