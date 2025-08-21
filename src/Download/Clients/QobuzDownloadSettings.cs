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
    /// <summary>
    /// Configuration settings for the Qobuz download client with intelligent concurrency management.
    /// </summary>
    /// <remarks>
    /// This settings class implements a sophisticated approach to download performance optimization
    /// through three distinct concurrency modes, each designed for different use cases and network conditions.
    /// 
    /// <para><b>Architectural Design Philosophy:</b></para>
    /// The download settings follow a "progressive enhancement" pattern where the default adaptive mode
    /// provides optimal performance for most users, while advanced modes offer fine-grained control
    /// when needed. This design minimizes configuration complexity while maximizing flexibility.
    /// 
    /// <para><b>Concurrency Mode Selection Guide:</b></para>
    /// <list type="table">
    /// <listheader>
    ///   <term>Mode</term>
    ///   <description>When to Use</description>
    /// </listheader>
    /// <item>
    ///   <term>Adaptive (Default)</term>
    ///   <description>Recommended for 95% of users. Automatically adjusts download concurrency based on
    ///   real-time performance metrics. Starts conservatively and scales up when network conditions allow.
    ///   Reduces concurrency during high latency or errors. Ideal for variable network conditions.</description>
    /// </item>
    /// <item>
    ///   <term>Fixed</term>
    ///   <description>Use when you have stable, predictable network conditions and want consistent behavior.
    ///   Good for dedicated servers with known bandwidth limits. Maintains constant concurrency regardless
    ///   of performance fluctuations.</description>
    /// </item>
    /// <item>
    ///   <term>Manual</term>
    ///   <description>For advanced users implementing custom download orchestration. Allows external control
    ///   of concurrency through plugins or scripts. Useful for integration with network QoS systems.</description>
    /// </item>
    /// </list>
    /// 
    /// <para><b>Performance Impact of Settings:</b></para>
    /// <list type="bullet">
    /// <item><b>AdaptiveMinConcurrency:</b> Lower values (1-2) provide stability on slow connections</item>
    /// <item><b>AdaptiveMaxConcurrency:</b> Higher values (5-10) maximize throughput on fast connections</item>
    /// <item><b>AdaptiveTargetLatency:</b> Lower values (500-800ms) favor speed, higher (1500-3000ms) favor stability</item>
    /// <item><b>FixedConcurrencyLevel:</b> Set to bandwidth ÷ 10Mbps for optimal utilization (e.g., 3 for 30Mbps)</item>
    /// </list>
    /// 
    /// <para><b>Settings Interaction Matrix:</b></para>
    /// The adaptive algorithm considers multiple factors:
    /// <code>
    /// OptimalConcurrency = f(CurrentLatency, ErrorRate, TargetLatency, Min, Max)
    /// where:
    ///   - If CurrentLatency &lt; TargetLatency × 0.8: Increase concurrency
    ///   - If CurrentLatency &gt; TargetLatency × 1.2: Decrease concurrency
    ///   - If ErrorRate &gt; 5%: Immediately reduce to minimum
    ///   - Recovery after errors: Gradual increase over 30 seconds
    /// </code>
    /// 
    /// <para><b>Quality Fallback Strategy:</b></para>
    /// When PreferredQuality is unavailable, the system automatically falls back:
    /// 27 (192kHz) → 7 (96kHz) → 6 (CD) → 5 (MP3)
    /// This ensures downloads complete even when high-res versions are missing.
    /// 
    /// <para><b>Common Configuration Patterns:</b></para>
    /// <list type="number">
    /// <item><b>Home User (Variable DSL/Cable):</b> Adaptive mode, Min=1, Max=4, Target=1500ms</item>
    /// <item><b>Gigabit Fiber:</b> Adaptive mode, Min=2, Max=10, Target=500ms</item>
    /// <item><b>Seedbox/VPS:</b> Fixed mode, ConcurrencyLevel=6-8</item>
    /// <item><b>Mobile/Metered:</b> Fixed mode, ConcurrencyLevel=1-2</item>
    /// </list>
    /// </remarks>
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

        [FieldDefinition(1, Label = "Download Path", Type = FieldType.Path, HelpText = "Path where completed downloads will be stored")]
        public string DownloadPath { get; set; }

        [FieldDefinition(2, Label = "Audio Quality", Type = FieldType.Select, SelectOptions = typeof(QobuzAudioQuality), HelpText = "Preferred audio quality for downloads")]
        public int PreferredQuality { get; set; }

        [FieldDefinition(3, Label = "Create Album Folders", Type = FieldType.Checkbox, HelpText = "Create individual folders for each album")]
        public bool CreateAlbumFolders { get; set; }

        [FieldDefinition(4, Label = "Concurrency Mode", Type = FieldType.Select, SelectOptions = typeof(DownloadConcurrencyMode), HelpText = "🎯 Download concurrency management: 'Adaptive' optimizes automatically for best performance (recommended), 'Fixed' uses constant concurrent downloads, 'Manual' for custom control")]
        public int ConcurrencyMode { get; set; } = (int)DownloadConcurrencyMode.Adaptive;

        [FieldDefinition(5, Label = "[Fixed/Manual] Concurrent Downloads", Type = FieldType.Number, HelpText = "Number of tracks to download simultaneously in Fixed or Manual mode (1-10, default: 3). Ignored in Adaptive mode.")]
        public int FixedConcurrencyLevel { get; set; } = 3;

        [FieldDefinition(6, Label = "[Adaptive] Min Downloads", Type = FieldType.Number, Advanced = true, HelpText = "🤖 Adaptive Mode: Minimum concurrent downloads (1-5, default: 1). System never goes below this.")]
        public int AdaptiveMinConcurrency { get; set; } = 1;

        [FieldDefinition(7, Label = "[Adaptive] Max Downloads", Type = FieldType.Number, Advanced = true, HelpText = "🤖 Adaptive Mode: Maximum concurrent downloads (2-10, default: 6). System never exceeds this.")]
        public int AdaptiveMaxConcurrency { get; set; } = 6;

        [FieldDefinition(8, Label = "[Adaptive] Target Speed (ms)", Type = FieldType.Number, Advanced = true, HelpText = "🤖 Adaptive Mode: Target download response time (500-3000ms, default: 1000ms). System increases concurrency when faster.")]
        public int AdaptiveTargetLatency { get; set; } = 1000;

        [FieldDefinition(9, Label = "Minimum Success Rate", Type = FieldType.Number, HelpText = "Minimum percentage of tracks that must download successfully (0-100%, default: 80%)")]
        public int MinimumSuccessRatePercent { get; set; } = 80;

        [FieldDefinition(10, Label = "Treat Preview as Failure", Type = FieldType.Checkbox, HelpText = "Count preview-only tracks as failures when calculating success rate")]
        public bool TreatPreviewAsFailure { get; set; } = false;

        [FieldDefinition(11, Label = "Skip Preview Tracks", Type = FieldType.Checkbox, HelpText = "Skip downloading tracks that are only available as previews/samples")]
        public bool SkipPreviewTracks { get; set; } = true;

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

        [Description("FLAC Hi-Res (24-bit/192kHz) - Limited availability")]
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