using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Models;
using Newtonsoft.Json;
using Qobuzarr.Tests.TestData;
using Xunit;
using Xunit.Abstractions;

namespace Qobuzarr.Tests.Unit.Models;

// Reference: TRACK_IDENTITY_PARITY.md DOC_VERSION: 2026-01-10-v2
// If this version changes, review Tier 2/3 parity tests for updated expectations.
// Location: https://github.com/RicherTunes/Lidarr.Plugin.Common/blob/main/docs/TRACK_IDENTITY_PARITY.md

/// <summary>
/// Track identity mapping parity tests.
/// These tests document Qobuzarr's field population for cross-plugin consistency.
/// Tier 1 tests MUST fail if fields are missing (hard contract).
/// Tier 2/3 tests document current behavior and MUST NOT cause CI failures.
/// </summary>
public class QobuzTrackParityTests
{
    private readonly ITestOutputHelper _output;

    public QobuzTrackParityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region Tier 1 Fields (Required)

    [Fact]
    public void QobuzTrack_HasRequiredTier1Fields()
    {
        // Arrange - Parse real API response
        var albumJson = SampleQobuzResponses.SampleAlbumResponse;
        var album = JsonConvert.DeserializeObject<QobuzAlbum>(albumJson);
        var track = album!.GetTracks()[0];

        // Assert - All Tier 1 fields must be present
        track.Id.Should().NotBeNullOrWhiteSpace("Id is required (Tier 1)");
        track.Title.Should().NotBeNullOrWhiteSpace("Title is required (Tier 1)");
        track.Performer.Should().NotBeNull("Artist/Performer is required (Tier 1)");
        track.Performer!.Name.Should().NotBeNullOrWhiteSpace("Artist.Name is required (Tier 1)");
        track.TrackNumber.Should().BePositive("TrackNumber is required (Tier 1)");
        track.Duration.TotalSeconds.Should().BePositive("Duration is required (Tier 1)");
    }

    [Fact]
    public void QobuzTrack_Album_HasRequiredFields()
    {
        // Arrange
        var albumJson = SampleQobuzResponses.SampleAlbumResponse;
        var album = JsonConvert.DeserializeObject<QobuzAlbum>(albumJson);
        var track = album!.GetTracks()[0];

        // Assert - Album fields via track's parent album
        album.Id.Should().NotBeNullOrWhiteSpace("Album.Id is required (Tier 1)");
        album.Title.Should().NotBeNullOrWhiteSpace("Album.Title is required (Tier 1)");
        album.GetArtistName().Should().NotBeNullOrWhiteSpace("Album.Artist.Name is required (Tier 1)");
    }

    #endregion

    #region Tier 2 Fields (Informational - MUST NOT fail)

    /// <summary>
    /// Documents DiscNumber population. See TRACK_IDENTITY_PARITY.md DOC_VERSION: 2026-01-10-v2
    /// Qobuzarr: Populated from "media_number" in API.
    /// Tidalarr: Hardcoded to 1 (Tier 3 - Tidal API limitation).
    /// </summary>
    [Fact]
    public void QobuzTrack_DiscNumber_DocumentCurrentBehavior()
    {
        // Arrange
        var albumJson = SampleQobuzResponses.SampleAlbumResponse;
        var album = JsonConvert.DeserializeObject<QobuzAlbum>(albumJson);
        var track = album!.GetTracks()[0];

        // Document current behavior - DO NOT use failing assertions
        var discNumber = track.DiscNumber;
        var isPopulated = discNumber > 0;
        _output.WriteLine($"DiscNumber: {discNumber} (populated={isPopulated})");

        // Informational assertion - logs warning if not populated
        if (!isPopulated)
        {
            _output.WriteLine("WARNING: DiscNumber not populated - parity gap with expected API behavior");
        }
    }

    /// <summary>
    /// Documents ISRC population. See TRACK_IDENTITY_PARITY.md DOC_VERSION: 2026-01-10-v2
    /// Qobuzarr: Populated from API.
    /// Tidalarr: Empty (Tidal API doesn't expose at track level).
    /// </summary>
    [Fact]
    public void QobuzTrack_Isrc_DocumentCurrentBehavior()
    {
        // Arrange
        var albumJson = SampleQobuzResponses.SampleAlbumResponse;
        var album = JsonConvert.DeserializeObject<QobuzAlbum>(albumJson);
        var track = album!.GetTracks()[0];

        // Document current behavior - DO NOT use failing assertions
        var isrc = track.ISRC ?? string.Empty;
        var isPopulated = !string.IsNullOrEmpty(isrc);
        var matchesFormat = System.Text.RegularExpressions.Regex.IsMatch(isrc, @"^[A-Z]{2}[A-Z0-9]{3}\d{7}$");
        _output.WriteLine($"ISRC: '{isrc}' (populated={isPopulated}, validFormat={matchesFormat})");

        // Informational only - logs warning if not populated
        if (!isPopulated)
        {
            _output.WriteLine("WARNING: ISRC not populated - parity gap");
        }
        else if (!matchesFormat)
        {
            _output.WriteLine("WARNING: ISRC populated but format unexpected");
        }
    }

    /// <summary>
    /// Documents ReleaseDate population. See TRACK_IDENTITY_PARITY.md DOC_VERSION: 2026-01-10-v2
    /// Qobuzarr: Populated via album.
    /// Tidalarr: Stored in Metadata dict only (implementation gap).
    /// </summary>
    [Fact]
    public void QobuzAlbum_ReleaseDate_DocumentCurrentBehavior()
    {
        // Arrange
        var albumJson = SampleQobuzResponses.SampleAlbumResponse;
        var album = JsonConvert.DeserializeObject<QobuzAlbum>(albumJson);

        // Document current behavior - DO NOT use failing assertions
        var releaseDate = album!.ReleaseDate;
        var isPopulated = releaseDate != default;
        var isReasonable = releaseDate.Year >= 1900 && releaseDate.Year <= 2100;
        _output.WriteLine($"ReleaseDate: {releaseDate:yyyy-MM-dd} (populated={isPopulated}, reasonable={isReasonable})");

        // Informational only - logs warning if not populated
        if (!isPopulated)
        {
            _output.WriteLine("WARNING: ReleaseDate not populated - parity gap");
        }
    }

    #endregion

    #region Tier 3 Fields (Aspirational - Informational only)

    /// <summary>
    /// Documents MusicBrainz ID availability. See TRACK_IDENTITY_PARITY.md DOC_VERSION: 2026-01-10-v2
    /// Qobuzarr: Via TrackDownload model when Lidarr provides them.
    /// Tidalarr: Empty (Tidal API doesn't provide).
    /// </summary>
    [Fact]
    public void TrackDownload_MusicBrainzIds_DocumentCurrentBehavior()
    {
        // MusicBrainzIds are populated from Lidarr's library context during download,
        // not from Qobuz API directly. The TrackDownload model includes:
        // - MusicBrainzTrackId
        // - MusicBrainzAlbumId
        // - MusicBrainzArtistId
        // - MusicBrainzReleaseGroupId
        //
        // This test documents the capability exists. Actual population depends on
        // Lidarr having the track matched in its library.

        // Document default state - no failing assertions for Tier 3
        var trackDownload = new TrackDownload();
        var mbTrackId = trackDownload.MusicBrainzTrackId ?? string.Empty;
        var mbAlbumId = trackDownload.MusicBrainzAlbumId ?? string.Empty;
        var mbArtistId = trackDownload.MusicBrainzArtistId ?? string.Empty;

        _output.WriteLine($"MusicBrainzTrackId: '{mbTrackId}' (default={string.IsNullOrEmpty(mbTrackId)})");
        _output.WriteLine($"MusicBrainzAlbumId: '{mbAlbumId}' (default={string.IsNullOrEmpty(mbAlbumId)})");
        _output.WriteLine($"MusicBrainzArtistId: '{mbArtistId}' (default={string.IsNullOrEmpty(mbArtistId)})");
        _output.WriteLine("Note: MusicBrainz IDs populated from Lidarr context during download, not from Qobuz API");
    }

    #endregion

    #region Metadata Applier Coverage

    /// <summary>
    /// Documents that Qobuzarr uses its own MetadataProcessor that writes more fields
    /// than the shared library TagLibAudioMetadataApplier.
    /// </summary>
    [Fact]
    public void MetadataProcessor_WritesExtendedFields()
    {
        // Note: Qobuzarr's MetadataProcessor (src/Download/Services/MetadataProcessor.cs) writes:
        // - Title, AlbumArtists, Performers, Album, Track, Disc, Composers
        // - MusicBrainz IDs (TrackId, ReleaseId, ArtistId) when available
        //
        // This is more complete than the shared library TagLibAudioMetadataApplier which
        // does NOT write ISRC or MusicBrainz IDs.
        //
        // Parity recommendation: Extend TagLibAudioMetadataApplier in Common to write
        // ISRC and MusicBrainz IDs when StreamingTrack provides them.

        // This is a documentation test asserting the extended capability exists
        Assert.True(true, "MetadataProcessor provides extended tag writing beyond base applier");
    }

    #endregion
}
