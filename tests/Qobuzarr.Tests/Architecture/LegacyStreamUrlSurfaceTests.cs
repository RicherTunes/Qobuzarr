using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Qobuzarr.Tests.Architecture
{
    public sealed class LegacyStreamUrlSurfaceTests
    {
        [Fact]
        public void ProductionCode_DoesNotCarryLegacyStreamUrlProviderForks()
        {
            var repoRoot = FindRepositoryRoot();
            var forbiddenFiles = new[]
            {
                "src/Download/Services/IStreamUrlProvider.cs",
                "src/Download/Services/StreamUrlProvider.cs",
                "src/Services/Interfaces/IStreamUrlProvider.cs",
                "src/Services/IQobuzStreamAvailabilityService.cs",
                "src/Services/QobuzStreamAvailabilityService.cs",
                "src/Services/BatchStreamingUrlProvider.cs",
            };

            var present = forbiddenFiles
                .Where(relativePath => File.Exists(Path.Combine(repoRoot, relativePath)))
                .ToArray();

            present.Should().BeEmpty(
                "stream URL resolution and restriction classification should stay centralized in " +
                "TrackDownloadService plus QobuzApiClient; stale alternate providers reintroduce " +
                "divergent permanent-vs-retryable parsing");
        }

        [Fact]
        public void Tests_DoNotKeepDisabledQobuzTrackDownloaderHusk()
        {
            var repoRoot = FindRepositoryRoot();
            var disabledTestPath = Path.Combine(
                repoRoot,
                "tests/Qobuzarr.Tests/Unit/Download/QobuzTrackDownloaderTests.cs");

            File.Exists(disabledTestPath).Should().BeFalse(
                "QobuzTrackDownloader was removed, so keeping a fully commented test file only preserves " +
                "dead API examples and makes future consolidation reviews noisier");
        }

        [Fact]
        public void ActiveDocs_DoNotDescribeRemovedStreamUrlOrDownloaderTypesAsCurrent()
        {
            var repoRoot = FindRepositoryRoot();
            var activeDocs = new[]
            {
                "docs/ARCHITECTURE.md",
                "docs/architecture/ARCHITECTURE.md",
                "docs/architecture/SERVICE-MIGRATION-GUIDE.md",
                "docs/VERIFICATION-REPORT.md",
            };
            var misleadingSnippets = new[]
            {
                "File downloads (QobuzTrackDownloader)",
                "`QobuzTrackDownloader`: Handles",
                "class IQobuzTrackDownloaderFactory",
                "class QobuzTrackDownloaderFactory",
                "class QobuzTrackDownloader",
                "BatchStreamingUrlProvider.GetStreamUrl()",
                "BatchStreamingUrlProvider.GetBatchStreamUrls()",
                "| `QobuzApiService` |",
                "Migrate `QobuzApiService`",
                "✅ **QobuzApiService**: Migrated",
            };

            var offenders = activeDocs
                .Select(relativePath => (relativePath, text: File.ReadAllText(Path.Combine(repoRoot, relativePath))))
                .SelectMany(doc => misleadingSnippets
                    .Where(snippet => doc.text.Contains(snippet, StringComparison.Ordinal))
                    .Select(snippet => $"{doc.relativePath}: {snippet}"))
                .ToArray();

            offenders.Should().BeEmpty(
                "active documentation should describe TrackDownloadService/QobuzDownloadOrchestrator as the current " +
                "download pipeline instead of deleted downloader or stream URL provider forks");
        }

        [Fact]
        public void ProductionSource_DoesNotReintroduceLegacyStreamUrlTypes()
        {
            var repoRoot = FindRepositoryRoot();
            var forbiddenTypeNames = new[]
            {
                "IStreamUrlProvider",
                "StreamUrlProvider",
                "IQobuzStreamAvailabilityService",
                "QobuzStreamAvailabilityService",
                "BatchStreamingUrlProvider",
                "IQobuzTrackDownloaderFactory",
                "QobuzTrackDownloaderFactory",
                "QobuzTrackDownloader",
                "QobuzStreamUrlService",
                "QobuzApiService",
            };

            var sourceRoots = new[] { "src", "QobuzCLI" };

            var offenders = sourceRoots
                .Select(root => Path.Combine(repoRoot, root))
                .Where(Directory.Exists)
                .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                .Select(relativeOrAbsolutePath => (
                    path: Path.GetRelativePath(repoRoot, relativeOrAbsolutePath).Replace('\\', '/'),
                    text: File.ReadAllText(relativeOrAbsolutePath)))
                .SelectMany(file => forbiddenTypeNames
                    .Where(typeName => file.text.Contains(typeName, StringComparison.Ordinal))
                    .Select(typeName => $"{file.path}: {typeName}"))
                .ToArray();

            offenders.Should().BeEmpty(
                "stream URL acquisition should flow through IQobuzApiClient/GetStreamingInfoAsync and the active " +
                "TrackDownloadService pipeline, not through resurrected legacy provider/factory/service surfaces");
        }

        [Fact]
        public void ProductionSource_KeepsRawStreamEndpointInsideApiLayer()
        {
            var repoRoot = FindRepositoryRoot();
            var allowedPrefixes = new[]
            {
                "src/API/",
                "src/Security/",
            };

            var offenders = Directory
                .EnumerateFiles(Path.Combine(repoRoot, "src"), "*.cs", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(repoRoot, path).Replace('\\', '/'))
                .Where(relativePath => !allowedPrefixes.Any(prefix =>
                    relativePath.StartsWith(prefix, StringComparison.Ordinal)))
                .Where(relativePath => File
                    .ReadAllText(Path.Combine(repoRoot, relativePath))
                    .Contains("track/getFileUrl", StringComparison.Ordinal))
                .ToArray();

            offenders.Should().BeEmpty(
                "provider-specific stream endpoint construction belongs in the API/signing layer; service/download " +
                "code should call IQobuzApiClient so restriction classification remains centralized");
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
