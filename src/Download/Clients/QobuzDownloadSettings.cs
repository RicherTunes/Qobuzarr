using System;
using System.ComponentModel;
using FluentValidation;
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

        // ===== BASIC SETTINGS SECTION =====
        [FieldDefinition(1, Label = "Download Folder", Type = FieldType.Path, Section = "Basic Settings", HelpText = "Where to save your music files")]
        public string DownloadPath { get; set; }

        [FieldDefinition(2, Label = "Audio Quality", Type = FieldType.Select, SelectOptions = typeof(QobuzAudioQuality), Section = "Basic Settings", HelpText = "Choose your preferred audio quality (higher = larger files)")]
        public int PreferredQuality { get; set; }

        [FieldDefinition(3, Label = "Organize by Album", Type = FieldType.Checkbox, Section = "Basic Settings", HelpText = "Create a separate folder for each album")]
        public bool CreateAlbumFolders { get; set; }

        // ===== PERFORMANCE SECTION =====
        [FieldDefinition(4, Label = "Download Speed", Type = FieldType.Select, SelectOptions = typeof(DownloadConcurrencyMode), Section = "Performance", HelpText = "How to manage simultaneous track downloads")]
        public int ConcurrencyMode { get; set; } = (int)DownloadConcurrencyMode.Adaptive;

        [FieldDefinition(5, Label = "Simultaneous Downloads", Type = FieldType.Number, Section = "Performance", HelpText = "How many tracks to download at once when using Fixed mode (1-10, default: 3)")]
        public int FixedConcurrencyLevel { get; set; } = 3;

        [FieldDefinition(6, Label = "Min Downloads", Type = FieldType.Number, Section = "Performance", Advanced = true, HelpText = "Minimum simultaneous downloads for Automatic mode (1-5, default: 1)")]
        public int AdaptiveMinConcurrency { get; set; } = 1;

        [FieldDefinition(7, Label = "Max Downloads", Type = FieldType.Number, Section = "Performance", Advanced = true, HelpText = "Maximum simultaneous downloads for Automatic mode (2-10, default: 6)")]
        public int AdaptiveMaxConcurrency { get; set; } = 6;

        [FieldDefinition(8, Label = "Target Speed (ms)", Type = FieldType.Number, Section = "Performance", Advanced = true, HelpText = "Target server response time for Automatic mode (500-3000ms, default: 1000)")]
        public int AdaptiveTargetLatency { get; set; } = 1000;

        // ===== DOWNLOAD BEHAVIOR SECTION =====  
        [FieldDefinition(9, Label = "Success Threshold", Type = FieldType.Number, Section = "Download Behavior", HelpText = "Consider album complete if this % of tracks download (0-100, default: 80)")]
        public int MinimumSuccessRatePercent { get; set; } = 80;

        [FieldDefinition(10, Label = "Skip Preview Tracks", Type = FieldType.Checkbox, Section = "Download Behavior", HelpText = "Don't download 30-second preview tracks")]
        public bool SkipPreviewTracks { get; set; } = true;
        
        [FieldDefinition(11, Label = "Preview Handling", Type = FieldType.Checkbox, Section = "Download Behavior", Advanced = true, HelpText = "Count preview-only tracks as download failures")]
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
        [Description("🤖 Automatic - Adjusts based on server speed")]
        Adaptive = 0,
        
        [Description("🔧 Fixed - Always download same number of tracks")]
        Fixed = 1,
        
        [Description("👨‍💻 Manual - Custom control (advanced)")]
        Manual = 2
    }

    public enum QobuzAudioQuality
    {
        [Description("🎧 MP3 320kbps - Smallest files, good quality")]
        MP3_320 = 5,

        [Description("💿 CD Quality - Lossless 16-bit/44.1kHz (recommended)")]
        FLAC_CD = 6,

        [Description("🎆 Hi-Res 96kHz - Studio quality 24-bit/96kHz")]
        FLAC_96 = 7,

        [Description("📎 Hi-Res 192kHz - Maximum quality (limited catalog)")]
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
            
            // Warn about format_id 27 having limited availability
            RuleFor(c => c.PreferredQuality)
                .Must(q => true) // Always passes, just for warning
                .When(c => c.PreferredQuality == 27)
                .WithMessage("Note: 24-bit/192kHz (format ID 27) has limited availability. " +
                           "Many tracks will fall back to 96kHz or CD quality. " + 
                           "This appears to be a Qobuz catalog/API limitation.")
                .WithSeverity(Severity.Info);

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

            // Validate that download path is accessible (if provided)
            RuleFor(c => c.DownloadPath)
                .Must(path => string.IsNullOrWhiteSpace(path) || IsValidPath(path))
                .WithMessage("Download path must be a valid directory path");
        }

        private bool IsValidPath(string path)
        {
            try
            {
                // Basic path validation - just check if it's a valid path format
                var fullPath = System.IO.Path.GetFullPath(path);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}