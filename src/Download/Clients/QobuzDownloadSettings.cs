using System;
using System.ComponentModel;
using FluentValidation;
using Lidarr.Plugin.Common.Services.Validation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Download;
using NzbDrone.Core.Validation;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Common.Extensions;

namespace Lidarr.Plugin.Qobuzarr.Download.Clients
{
    public class QobuzDownloadSettings : IProviderConfig
    {
        private static readonly QobuzDownloadSettingsValidator Validator = new QobuzDownloadSettingsValidator();

        public QobuzDownloadSettings()
        {
            DownloadPath = "";
            PreferredQuality = 6; // FLAC CD Quality default
            CreateAlbumFolders = true;
            ConcurrencyMode = (int)DownloadConcurrencyMode.Adaptive; // Adaptive by default
            FixedConcurrencyLevel = 3; // Conservative default for safety
            AdaptiveMinConcurrency = 1;
            AdaptiveMaxConcurrency = 6;
            AdaptiveTargetLatency = 1000;
        }

        // === STORAGE SETTINGS ===
        [FieldDefinition(1, Label = "Download Path", Type = FieldType.Path, Section = "Storage", HelpText = "Root folder where all downloads will be saved. Lidarr will organize files from here into your media library.")]
        public string DownloadPath { get; set; }

        [FieldDefinition(2, Label = "Create Album Folders", Type = FieldType.Checkbox, Section = "Storage", HelpText = "Organize downloads into Artist/Album folder structure. When enabled: Artist/Album/tracks. When disabled: All tracks in download path.")]
        public bool CreateAlbumFolders { get; set; }

        // === QUALITY SETTINGS ===
        [FieldDefinition(3, Label = "Audio Quality", Type = FieldType.Select, SelectOptions = typeof(QobuzAudioQuality), Section = "Quality", HelpText = "Preferred audio quality. The plugin will automatically fall back to lower qualities if your selection is unavailable. Note: Your Qobuz subscription determines maximum available quality.")]
        public int PreferredQuality { get; set; }

        // === CONCURRENCY SETTINGS ===
        [FieldDefinition(4, Label = "Concurrency Mode", Type = FieldType.Select, SelectOptions = typeof(DownloadConcurrencyMode), Section = "Performance", HelpText = "How to manage parallel track downloads. 'Adaptive' automatically adjusts based on server speed (recommended). 'Fixed' uses a constant number of parallel downloads.")]
        public int ConcurrencyMode { get; set; } = (int)DownloadConcurrencyMode.Adaptive;

        [FieldDefinition(5, Label = "Fixed Concurrent Downloads", Type = FieldType.Number, Section = "Performance", HelpText = "Number of tracks to download simultaneously when using Fixed mode. Higher = faster but may cause server throttling. Range: 1-10, Default: 3")]
        public int FixedConcurrencyLevel { get; set; } = 3;

        [FieldDefinition(6, Label = "Minimum Downloads", Type = FieldType.Number, Section = "Performance", Advanced = true, HelpText = "[Adaptive Mode] Minimum parallel track downloads. System won't go below this even if server is slow. Range: 1-5, Default: 1")]
        public int AdaptiveMinConcurrency { get; set; } = 1;

        [FieldDefinition(7, Label = "Maximum Downloads", Type = FieldType.Number, Section = "Performance", Advanced = true, HelpText = "[Adaptive Mode] Maximum parallel track downloads. System won't exceed this even if server is fast. Range: 2-10, Default: 6")]
        public int AdaptiveMaxConcurrency { get; set; } = 6;

        [FieldDefinition(8, Label = "Target Response Time (ms)", Type = FieldType.Number, Section = "Performance", Advanced = true, HelpText = "[Adaptive Mode] Ideal download response time. System increases concurrency when faster than this. Range: 500-3000ms, Default: 1000ms")]
        public int AdaptiveTargetLatency { get; set; } = 1000;

        // === RELIABILITY SETTINGS ===
        [FieldDefinition(9, Label = "Minimum Success Rate (%)", Type = FieldType.Number, Section = "Reliability", HelpText = "Minimum percentage of tracks that must download successfully for the album to be considered complete. If below this threshold, the download fails. Range: 0-100%, Default: 80%")]
        public int MinimumSuccessRatePercent { get; set; } = 80;

        [FieldDefinition(10, Label = "Skip Preview Tracks", Type = FieldType.Checkbox, Section = "Reliability", HelpText = "Skip 30-second preview/sample tracks instead of downloading them. Recommended for better success rates.")]
        public bool SkipPreviewTracks { get; set; } = true;

        [FieldDefinition(11, Label = "Count Previews as Failures", Type = FieldType.Checkbox, Section = "Reliability", Advanced = true, HelpText = "When calculating success rate, count skipped preview tracks as failures. Enable this for stricter quality control.")]
        public bool TreatPreviewAsFailure { get; set; } = false;

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }

        /// <summary>
        /// Get quality description for UI display
        /// </summary>
        public string GetQualityDescription()
        {
            return PreferredQuality switch
            {
                5 => "MP3 320kbps",
                6 => "FLAC CD Quality (16-bit/44.1kHz)",
                7 => "FLAC Hi-Res (24-bit/96kHz)",
                27 => "FLAC Hi-Res (24-bit/192kHz)",
                _ => "Unknown Quality"
            };
        }

        /// <summary>
        /// Check if quality is lossless
        /// </summary>
        public bool IsLosslessQuality()
        {
            return PreferredQuality != 5; // Everything except MP3 is lossless
        }

        /// <summary>
        /// Get the download policy based on current settings.
        /// </summary>
        public DownloadPolicy GetDownloadPolicy()
        {
            return new DownloadPolicy
            {
                MinimumSuccessRate = MinimumSuccessRatePercent / 100.0,
                TreatPreviewAsFailure = TreatPreviewAsFailure,
                SkipPreviewTracks = SkipPreviewTracks,
                MaxConcurrentTrackDownloads = GetEffectiveConcurrency(),
                ContinueOnTrackFailure = true,
                EnableQualityFallback = true,
                FailOnNoTracksAvailable = true
            };
        }

        /// <summary>
        /// Get the effective concurrency level based on the current mode
        /// </summary>
        public int GetEffectiveConcurrency()
        {
            return ConcurrencyMode switch
            {
                (int)DownloadConcurrencyMode.Fixed => FixedConcurrencyLevel,
                (int)DownloadConcurrencyMode.Manual => FixedConcurrencyLevel, // Use fixed value for manual mode
                _ => Math.Min(Environment.ProcessorCount / 2, AdaptiveMaxConcurrency) // Adaptive default
            };
        }

        /// <summary>
        /// Check if adaptive concurrency is enabled
        /// </summary>
        public bool IsAdaptiveConcurrencyEnabled()
        {
            return ConcurrencyMode == (int)DownloadConcurrencyMode.Adaptive;
        }

        /// <summary>
        /// Backward compatibility property for MaxConcurrentDownloads
        /// </summary>
        public int MaxConcurrentDownloads => GetEffectiveConcurrency();
    }

    public enum DownloadConcurrencyMode
    {
        [Description("🤖 Adaptive (Recommended) - Optimizes download performance automatically")]
        Adaptive = 0,

        [Description("🔧 Fixed - Uses constant number of concurrent downloads")]
        Fixed = 1,

        [Description("👨‍💻 Manual (Advanced) - For custom download management")]
        Manual = 2
    }

    public enum QobuzAudioQuality
    {
        [Description("MP3 320kbps")]
        MP3_320 = 5,

        [Description("FLAC CD Quality (16-bit/44.1kHz)")]
        FLAC_CD = 6,

        [Description("FLAC Hi-Res (24-bit/96kHz)")]
        FLAC_96 = 7,

        [Description("FLAC Hi-Res (24-bit/192kHz) - Purchase only, not available for streaming")]
        FLAC_192 = 27
    }

    public class QobuzDownloadSettingsValidator : AbstractValidator<QobuzDownloadSettings>
    {
        public QobuzDownloadSettingsValidator()
        {
            RuleFor(c => c.DownloadPath)
                .NotEmpty()
                .WithMessage("Download path is required");

            RuleFor(c => c.PreferredQuality)
                .Must(q => q == 5 || q == 6 || q == 7 || q == 27)
                .WithMessage("Invalid audio quality selection");

            // Warn about format_id 27 not being available for streaming
            RuleFor(c => c.PreferredQuality)
                .Must(q => true) // Always passes, just for warning
                .When(c => c.PreferredQuality == 27)
                .WithMessage("⚠️ WARNING: 24-bit/192kHz (format ID 27) is NOT available for streaming. " +
                           "This quality level is only available when you purchase and download albums directly from Qobuz. " +
                           "For streaming, the maximum available quality is 24-bit/96kHz (format ID 7). " +
                           "Your downloads will automatically fall back to 96kHz or CD quality.")
                .WithSeverity(Severity.Warning);

            // Concurrency validation
            RuleFor(c => c.FixedConcurrencyLevel)
                .InclusiveBetween(1, 10)
                .WithMessage("Fixed concurrency level must be between 1 and 10");

            RuleFor(c => c.AdaptiveMinConcurrency)
                .InclusiveBetween(1, 5)
                .WithMessage("Adaptive minimum concurrency must be between 1 and 5");

            RuleFor(c => c.AdaptiveMaxConcurrency)
                .InclusiveBetween(2, 10)
                .WithMessage("Adaptive maximum concurrency must be between 2 and 10");

            // Adaptive max must be greater than min
            RuleFor(c => c.AdaptiveMaxConcurrency)
                .GreaterThan(c => c.AdaptiveMinConcurrency)
                .WithMessage("Adaptive maximum concurrency must be greater than minimum concurrency");

            RuleFor(c => c.AdaptiveTargetLatency)
                .InclusiveBetween(500, 3000)
                .WithMessage("Target download latency must be between 500 and 3000 milliseconds");

            RuleFor(c => c.MinimumSuccessRatePercent)
                .InclusiveBetween(0, 100)
                .WithMessage("Minimum success rate must be between 0 and 100");

            // Validate that download path is well-formed. Delegates to common's
            // DownloadPathValidator so every RicherTunes streaming plugin gives
            // the same first-run UX (rejects traversal segments, relative paths,
            // tilde-expansion, embedded NULs). The NotEmpty rule above handles
            // the empty case; this rule short-circuits on empty input so it
            // doesn't double-report.
            RuleFor(c => c.DownloadPath)
                .Must(path => string.IsNullOrWhiteSpace(path) || DownloadPathValidator.Validate(path).IsValid)
                .WithMessage(c => DownloadPathValidator.Validate(c.DownloadPath ?? string.Empty).Message);
        }
    }
}
