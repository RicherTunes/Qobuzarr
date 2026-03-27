using Lidarr.Plugin.Common.Base;

namespace Lidarr.Plugin.Qobuzarr.Integration;

/// <summary>
/// Strongly typed settings for the Qobuzarr streaming bridge plugin.
/// Extends <see cref="BaseStreamingSettings"/> with Qobuz-specific fields.
/// These settings are managed by the StreamingPlugin settings provider and
/// exposed to the host via the ISettingsProvider bridge contract.
/// </summary>
public sealed class QobuzarrStreamingSettings : BaseStreamingSettings
{
    public QobuzarrStreamingSettings()
    {
        BaseUrl = "https://www.qobuz.com/api.json/0.2";
        Email = string.Empty;
        Password = string.Empty;
        DownloadPath = string.Empty;
        PreferredQuality = 6; // FLAC-CD
        CountryCode = "US";
        SearchLimit = 100;
    }

    /// <summary>
    /// Root folder where downloaded music will be saved.
    /// </summary>
    public string DownloadPath { get; set; }

    /// <summary>
    /// Audio quality preference:
    /// 5 = MP3-320, 6 = FLAC-CD, 7 = FLAC-Hi-Res, 27 = FLAC-Max.
    /// </summary>
    public int PreferredQuality { get; set; }

    /// <inheritdoc />
    public override bool IsValid(out string errorMessage)
    {
        if (!base.IsValid(out errorMessage))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(Email))
        {
            errorMessage = "Email is required for Qobuz authentication.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            errorMessage = "Password is required for Qobuz authentication.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(DownloadPath))
        {
            errorMessage = "Download path is required.";
            return false;
        }

        int[] validQualities = [5, 6, 7, 27];
        if (Array.IndexOf(validQualities, PreferredQuality) < 0)
        {
            errorMessage = "Preferred quality must be one of: 5 (MP3-320), 6 (FLAC-CD), 7 (FLAC-Hi-Res), 27 (FLAC-Max).";
            return false;
        }

        errorMessage = null!;
        return true;
    }
}
