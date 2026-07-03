using Lidarr.Plugin.Abstractions.Models;
using Lidarr.Plugin.Common.TestKit.Compliance;
using Lidarr.Plugin.Qobuzarr.Download.Services;
using Qobuzarr.Tests.Builders;

namespace Qobuzarr.Tests.Compliance;

/// <summary>
/// Qobuzarr's adoption of the cross-plugin <c>cover-art-embedding</c> parity axis. Drives the real
/// <see cref="QobuzStreamingTrack.From"/> download-path carrier and asserts the album exposes a
/// fetchable cover URL (mapped from <c>QobuzAlbum.Image</c>) via GetBestCoverArtUrl(), so Common's
/// SimpleDownloadOrchestrator can embed the album cover. Pins the cover-URL enabler against a future
/// regression to art-less downloads.
/// </summary>
public sealed class QobuzCoverArtEmbeddingTests : CoverArtEmbeddingComplianceTestBase
{
    protected override StreamingAlbum BuildDownloadPathAlbumWithCover() =>
        QobuzStreamingTrack.From(QobuzTrackBuilder.New().Build(), QobuzAlbumBuilder.New().Build()).Album;

    protected override StreamingAlbum BuildDownloadPathAlbumWithoutCover()
    {
        var album = QobuzAlbumBuilder.New().Build();
        album.Image = null;
        return QobuzStreamingTrack.From(QobuzTrackBuilder.New().Build(), album).Album;
    }
}
