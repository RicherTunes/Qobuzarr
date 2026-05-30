using FluentAssertions;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Xunit;

namespace Qobuzarr.Tests.Unit.Download
{
    /// <summary>
    /// Pins the canonical lyrics gating defaults exposed to the user, matching tidalarr:
    /// SaveSyncedLyrics is the master toggle (on); UseLRCLIB gates the LRCLIB fallback and is
    /// off by default so no third-party request is made unless the user opts in.
    /// </summary>
    public class QobuzDownloadSettingsLyricsTests
    {
        [Fact]
        public void Lyrics_settings_default_to_master_on_and_lrclib_opt_in_off()
        {
            var settings = new QobuzDownloadSettings();

            settings.SaveSyncedLyrics.Should().BeTrue("synced-lyrics saving is the master toggle (on by default)");
            settings.UseLRCLIB.Should().BeFalse("LRCLIB is an opt-in third-party fallback (off by default)");
        }
    }
}
