using FluentValidation.TestHelper;
using Lidarr.Plugin.Qobuzarr.Download.Clients;
using Xunit;

namespace Qobuzarr.Tests.Unit.Download
{
    /// <summary>
    /// Regression guards for QobuzDownloadSettingsValidator's DownloadPath rule.
    ///
    /// Previously the validator only called Path.GetFullPath inside a try/catch
    /// and accepted anything that didn't throw — which let traversal segments
    /// (../etc/passwd) and relative paths (downloads/qobuz) through. Users hit
    /// confusing runtime errors instead of a clear save-time validation message.
    ///
    /// The validator now delegates to common's DownloadPathValidator, which
    /// gives uniform first-run UX across all RicherTunes streaming plugins.
    /// </summary>
    public sealed class QobuzDownloadPathValidationTests
    {
        private static QobuzDownloadSettings ValidBase => new()
        {
            DownloadPath = System.IO.Path.GetTempPath(),
            PreferredQuality = 6,
            FixedConcurrencyLevel = 3,
            AdaptiveMinConcurrency = 1,
            AdaptiveMaxConcurrency = 5,
            AdaptiveTargetLatency = 1000,
            MinimumSuccessRatePercent = 90
        };

        private static readonly QobuzDownloadSettingsValidator Validator = new();

        [Fact]
        public void DownloadPath_AbsoluteTempPath_NoError()
        {
            var settings = ValidBase;
            Validator.TestValidate(settings).ShouldNotHaveValidationErrorFor(s => s.DownloadPath);
        }

        [Fact]
        public void DownloadPath_Empty_FlagsRequired()
        {
            var settings = ValidBase;
            settings.DownloadPath = string.Empty;
            Validator.TestValidate(settings).ShouldHaveValidationErrorFor(s => s.DownloadPath);
        }

        [Fact]
        public void DownloadPath_RelativePath_Rejected()
        {
            // Previous validator silently accepted relative paths because
            // Path.GetFullPath resolves them against the process working
            // directory. Users hit "Access denied" later instead of a clear
            // "use an absolute path" message at save time.
            var settings = ValidBase;
            settings.DownloadPath = "downloads/qobuz";
            Validator.TestValidate(settings).ShouldHaveValidationErrorFor(s => s.DownloadPath);
        }

        [Fact]
        public void DownloadPath_ContainsTraversal_Rejected()
        {
            // Path-traversal segments are rejected to prevent users from
            // accidentally configuring an escape path and to make the failure
            // mode diagnostic rather than file-system-permission-dependent.
            var settings = ValidBase;
            settings.DownloadPath = System.OperatingSystem.IsWindows()
                ? "C:\\downloads\\..\\etc"
                : "/downloads/../etc";
            Validator.TestValidate(settings).ShouldHaveValidationErrorFor(s => s.DownloadPath);
        }

        [Fact]
        public void DownloadPath_EmbeddedNul_Rejected()
        {
            var settings = ValidBase;
            settings.DownloadPath = "/downloads\0/qobuz";
            Validator.TestValidate(settings).ShouldHaveValidationErrorFor(s => s.DownloadPath);
        }

        [Fact]
        public void DownloadPath_LeadingTildeOnUnix_Rejected()
        {
            // Tilde shell-expansion only happens in a shell; Lidarr's process
            // doesn't expand it. Reject up-front so users get a clear error
            // instead of "file not found" later.
            if (System.OperatingSystem.IsWindows()) return;
            var settings = ValidBase;
            settings.DownloadPath = "~/Music";
            Validator.TestValidate(settings).ShouldHaveValidationErrorFor(s => s.DownloadPath);
        }
    }
}
