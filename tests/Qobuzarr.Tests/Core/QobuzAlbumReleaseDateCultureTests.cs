using System;
using System.Globalization;
using System.Threading;
using FluentAssertions;
using Xunit;
using Lidarr.Plugin.Qobuzarr.Models;

namespace Qobuzarr.Tests.Core
{
    /// <summary>
    /// QobuzAlbum.ReleaseDate falls back to parsing the API's ISO date strings (ReleaseDateOriginal /
    /// ReleaseDateStream) when no unix timestamp is present. Those strings are always Gregorian (e.g.
    /// "2021-05-14"), so they MUST be parsed with the invariant culture. Parsing under the current culture's
    /// calendar (e.g. a Thai Buddhist locale) shifts the year (2021 BE → 1478 CE), corrupting the album's
    /// release date in Lidarr. (Same class as the apple non-Gregorian release-date fix.)
    /// </summary>
    public class QobuzAlbumReleaseDateCultureTests
    {
        [Fact]
        public void ReleaseDate_FromIsoString_IsCultureInvariant_UnderNonGregorianCalendar()
        {
            var original = Thread.CurrentThread.CurrentCulture;
            try
            {
                // Force a non-Gregorian (Thai Buddhist) calendar on the current culture so the shift is
                // deterministic regardless of the runner's ICU defaults.
                var thai = (CultureInfo)new CultureInfo("th-TH").Clone();
                thai.DateTimeFormat.Calendar = new ThaiBuddhistCalendar();
                Thread.CurrentThread.CurrentCulture = thai;

                var album = new QobuzAlbum
                {
                    ReleasedAtTimestamp = 0,            // force the string-parse fallback
                    ReleaseDateOriginal = "2021-05-14",
                };

                album.ReleaseDate.Year.Should().Be(2021,
                    "the release-date year must be Gregorian, not shifted by the locale's calendar");
                album.ReleaseDate.Month.Should().Be(5);
                album.ReleaseDate.Day.Should().Be(14);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }
    }
}
