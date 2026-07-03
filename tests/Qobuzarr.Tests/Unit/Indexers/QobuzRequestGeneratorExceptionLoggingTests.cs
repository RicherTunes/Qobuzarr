using System;
using System.Linq;
using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Indexers;
using Qobuzarr.Tests.Helpers;

namespace Qobuzarr.Tests.Unit.Indexers;

/// <summary>
/// Characterization tests verifying that request-generation exceptions in
/// QobuzRequestGenerator are OBSERVABLE — logged at Error level — rather than
/// silently swallowed. These tests were written in response to audit finding F10
/// which claimed the catch sites were silent; the tests confirm the finding is
/// INCORRECT: all three top-level catch blocks already call _logger.Error before
/// returning the empty chain. Tests document and pin this correct behavior.
/// </summary>
public sealed class QobuzRequestGeneratorExceptionLoggingTests
{
    // ── GetRecentRequests: session-delegate throws ───────────────────────────
    // The getSession delegate is called inside the try block at line ~154.
    // When it throws the catch at line ~170 must log the error and return empty.

    [Fact]
    public void GetRecentRequests_WhenSessionDelegateThrows_LogsErrorAndReturnsEmptyChain()
    {
        // Arrange
        TestLogger.ClearLoggedMessages();
        var logger = TestLogger.Create("F10-GetRecentRequests");

        var throwingGetSession = new Func<Lidarr.Plugin.Qobuzarr.Models.Authentication.QobuzSession>(
            () => throw new InvalidOperationException("injected session failure"));

        var generator = new QobuzRequestGenerator(
            new QobuzIndexerSettings(),
            logger,
            throwingGetSession);

        // Act — must NOT throw; catch block handles it
        var chain = generator.GetRecentRequests();

        // Assert: empty chain (behavior preserved)
        chain.GetAllTiers().Should().BeEmpty(
            "when request generation fails the chain must be empty, not null or partial");

        // Assert: failure was logged (observable, not silent)
        var messages = TestLogger.GetLoggedMessages();
        messages.Should().Contain(m => m.Contains("Error generating recent requests"),
            "the catch block must emit an Error log so operators can diagnose request-generation failures");
    }

    // ── GetSearchRequests(Album): session throws inside CreateIndexerRequests ─
    // The _getSession?.Invoke() inside CreateIndexerRequests propagates up through
    // the try/catch in GetSearchRequests(AlbumSearchCriteria) at line ~100.

    [Fact]
    public void GetSearchRequests_Album_WhenSessionDelegateThrows_LogsErrorAndReturnsEmptyChain()
    {
        // Arrange
        TestLogger.ClearLoggedMessages();
        var logger = TestLogger.Create("F10-GetSearchRequests-Album");

        var throwingGetSession = new Func<Lidarr.Plugin.Qobuzarr.Models.Authentication.QobuzSession>(
            () => throw new InvalidOperationException("injected session failure"));

        var generator = new QobuzRequestGenerator(
            new QobuzIndexerSettings(),
            logger,
            throwingGetSession);

        var criteria = new NzbDrone.Core.IndexerSearch.Definitions.AlbumSearchCriteria
        {
            Artist = new NzbDrone.Core.Music.Artist { Name = "Test Artist" },
            Albums = new System.Collections.Generic.List<NzbDrone.Core.Music.Album>
            {
                new NzbDrone.Core.Music.Album { Title = "Test Album" },
            },
        };

        // Act — must NOT throw
        var chain = generator.GetSearchRequests(criteria);

        // Assert: empty chain
        chain.GetAllTiers().Should().BeEmpty();

        // Assert: error logged
        var messages = TestLogger.GetLoggedMessages();
        messages.Should().Contain(m => m.Contains("Error generating album search requests"),
            "the catch block must emit an Error log for album request-generation failures");
    }

    // ── GetSearchRequests(Artist): session throws inside CreateIndexerRequests ─

    [Fact]
    public void GetSearchRequests_Artist_WhenSessionDelegateThrows_LogsErrorAndReturnsEmptyChain()
    {
        // Arrange
        TestLogger.ClearLoggedMessages();
        var logger = TestLogger.Create("F10-GetSearchRequests-Artist");

        var throwingGetSession = new Func<Lidarr.Plugin.Qobuzarr.Models.Authentication.QobuzSession>(
            () => throw new InvalidOperationException("injected session failure"));

        var generator = new QobuzRequestGenerator(
            new QobuzIndexerSettings(),
            logger,
            throwingGetSession);

        var criteria = new NzbDrone.Core.IndexerSearch.Definitions.ArtistSearchCriteria
        {
            Artist = new NzbDrone.Core.Music.Artist { Name = "Test Artist" },
        };

        // Act — must NOT throw
        var chain = generator.GetSearchRequests(criteria);

        // Assert: empty chain
        chain.GetAllTiers().Should().BeEmpty();

        // Assert: error logged
        var messages = TestLogger.GetLoggedMessages();
        messages.Should().Contain(m => m.Contains("Error generating artist search requests"),
            "the catch block must emit an Error log for artist request-generation failures");
    }
}
