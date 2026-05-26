using System;
using Lidarr.Plugin.Qobuzarr.Core;
using Xunit;

namespace Qobuzarr.Tests.Unit.Download
{
    /// <summary>
    /// Tests for QobuzQualityPolicyEnforcer — gates the post-API quality
    /// selection against the user's "Enable Quality Fallback" preference.
    ///
    /// Without this enforcer, the download path silently accepted whatever
    /// quality the Qobuz API returned (typically falling back to CD when
    /// HiRes wasn't licensed for the user's region or track). Users who
    /// pay for Sublime+ but receive Studio Premier tracks had no signal
    /// — the download just silently dropped to lower bitrate.
    ///
    /// With strict mode (EnableQualityFallback=false), the enforcer throws
    /// a clear InvalidOperationException naming both qualities, so the
    /// download fails fast and the user sees a meaningful error in the
    /// Lidarr UI ("Strict quality mode: requested 27 but got 6").
    /// </summary>
    public sealed class QobuzQualityPolicyEnforcerTests
    {
        [Fact]
        public void Enforce_FallbackAllowed_QualityMatches_DoesNotThrow()
        {
            QobuzQualityPolicyEnforcer.Enforce(selectedQuality: 6, preferredQuality: 6, allowFallback: true);
        }

        [Fact]
        public void Enforce_FallbackAllowed_QualityMismatch_DoesNotThrow()
        {
            // Default behaviour — silently accept fallback. The download path
            // already logs an Info message when this happens; not the
            // enforcer's job to throw.
            QobuzQualityPolicyEnforcer.Enforce(selectedQuality: 6, preferredQuality: 27, allowFallback: true);
        }

        [Fact]
        public void Enforce_Strict_QualityMatches_DoesNotThrow()
        {
            QobuzQualityPolicyEnforcer.Enforce(selectedQuality: 27, preferredQuality: 27, allowFallback: false);
        }

        [Fact]
        public void Enforce_Strict_QualityMismatch_Throws()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                QobuzQualityPolicyEnforcer.Enforce(selectedQuality: 6, preferredQuality: 27, allowFallback: false));

            // Message must name BOTH qualities so the user can see what they
            // asked for vs what they got.
            Assert.Contains("27", ex.Message);
            Assert.Contains("6", ex.Message);
            // Must reference the strict-mode setting by its UI name so the user
            // knows which toggle to flip to allow fallback.
            Assert.Contains("Quality Fallback", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(5, 6)]    // MP3 vs FLAC CD
        [InlineData(6, 7)]    // FLAC CD vs FLAC HiRes 96
        [InlineData(7, 27)]   // FLAC HiRes 96 vs FLAC Max 192
        public void Enforce_Strict_NamesBothFormatsByQualityId(int selected, int preferred)
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                QobuzQualityPolicyEnforcer.Enforce(selected, preferred, allowFallback: false));

            Assert.Contains(selected.ToString(), ex.Message);
            Assert.Contains(preferred.ToString(), ex.Message);
        }
    }
}
