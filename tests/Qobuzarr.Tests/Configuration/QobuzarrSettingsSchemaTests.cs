using System.Linq;
using Lidarr.Plugin.Qobuzarr.Integration;
using Microsoft.Extensions.Logging.Abstractions;
using NLog;
using Xunit;

namespace Qobuzarr.Tests.Configuration;

/// <summary>
/// Wave 65 TDD: settings field descriptions must give users enough information
/// to fill them out correctly without consulting external docs. These tests
/// pin concrete UX contracts on the SettingDefinition descriptions surfaced
/// to the Lidarr UI.
/// </summary>
public sealed class QobuzarrSettingsSchemaTests
{
    private readonly QobuzarrStreamingPlugin _plugin = new();

    [Fact]
    public void PreferredQuality_DescriptionIncludesHumanReadableQualityNames()
    {
        // Pre-fix description: "Audio quality preference (5=MP3-320, 6=FLAC-CD,
        // 7=FLAC-Hi-Res, 27=FLAC-Max)." — terse, doesn't tell user that the code
        // is what gets sent OR that not all codes work on all subscription tiers.
        var def = GetDefinition("PreferredQuality");

        Assert.NotNull(def.Description);
        var desc = def.Description!;
        // Must include actual quality names users recognize
        Assert.Contains("FLAC", desc, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MP3", desc, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PreferredQuality_DescriptionWarnsAboutSubscriptionTier()
    {
        // Common support burden: users on a low-tier sub set 27 (max) and
        // can't understand why downloads stay at 6. Mention this up front.
        var def = GetDefinition("PreferredQuality");

        var desc = def.Description ?? string.Empty;
        Assert.True(
            desc.Contains("subscription", System.StringComparison.OrdinalIgnoreCase) ||
            desc.Contains("plan", System.StringComparison.OrdinalIgnoreCase) ||
            desc.Contains("fallback", System.StringComparison.OrdinalIgnoreCase) ||
            desc.Contains("tier", System.StringComparison.OrdinalIgnoreCase),
            $"PreferredQuality should warn about subscription-tier mismatch: {desc}");
    }

    [Fact]
    public void PreferredQuality_HasSensibleDefaultThatWorksForFreeTier()
    {
        var def = GetDefinition("PreferredQuality");
        // CD-quality FLAC (6) is the broadest-compatible default — works on all
        // tiers that include lossless. Avoid 27 (max) as default since it would
        // silently downgrade for most users.
        Assert.Equal(6, def.DefaultValue);
    }

    [Fact]
    public void Email_DescriptionMentionsSubscription()
    {
        // Wave 85 UX TDD: pre-fix description was "Qobuz account email address for
        // authentication." — user knows it's a Qobuz email but doesn't know if
        // free Qobuz accounts work. Description should mention subscription so
        // users on a free tier (no streaming) don't waste time troubleshooting.
        var def = GetDefinition("Email");
        Assert.NotNull(def.Description);
        Assert.True(
            def.Description!.Contains("subscri", System.StringComparison.OrdinalIgnoreCase) ||
            def.Description.Contains("paid", System.StringComparison.OrdinalIgnoreCase) ||
            def.Description.Contains("Studio", System.StringComparison.OrdinalIgnoreCase),
            $"Email description should mention subscription requirement: {def.Description}");
    }

    [Fact]
    public void Password_DescriptionDistinguishesFromApiKey()
    {
        // Users sometimes paste their app secret or API key into the password
        // field. Description should explicitly say "account password" (not API
        // key) so the wrong-paste case is clear.
        var def = GetDefinition("Password");
        Assert.NotNull(def.Description);
        Assert.Contains("password", def.Description!, System.StringComparison.OrdinalIgnoreCase);
        // Should NOT just say "Qobuz account password" — should add hint that
        // it's the same one used to log in to qobuz.com.
        Assert.True(
            def.Description.Contains("login", System.StringComparison.OrdinalIgnoreCase) ||
            def.Description.Contains("log in", System.StringComparison.OrdinalIgnoreCase) ||
            def.Description.Contains("qobuz.com", System.StringComparison.OrdinalIgnoreCase) ||
            def.Description.Contains("not an", System.StringComparison.OrdinalIgnoreCase),
            $"Password description should clarify it's the login password, not an API key: {def.Description}");
    }

    [Fact]
    public void CountryCode_DescriptionGivesExamples()
    {
        var def = GetDefinition("CountryCode");
        Assert.NotNull(def.Description);
        // Examples like "US, CA, GB" save the user a Wikipedia trip.
        Assert.True(
            def.Description!.Contains("US", System.StringComparison.Ordinal) ||
            def.Description.Contains("e.g.", System.StringComparison.OrdinalIgnoreCase),
            $"CountryCode description should include examples: {def.Description}");
    }

    private Lidarr.Plugin.Abstractions.Contracts.SettingDefinition GetDefinition(string key)
    {
        // DescribeSettings is protected; access via reflection to avoid the
        // Initialize() ceremony just for a schema-shape assertion.
        var method = typeof(Lidarr.Plugin.Common.Hosting.StreamingPlugin<,>)
            .MakeGenericType(
                typeof(QobuzarrStreamingPlugin).BaseType!.GetGenericArguments()[0],
                typeof(QobuzarrStreamingPlugin).BaseType!.GetGenericArguments()[1])
            .GetMethod("DescribeSettings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var defs = (System.Collections.Generic.IEnumerable<Lidarr.Plugin.Abstractions.Contracts.SettingDefinition>)method.Invoke(_plugin, null)!;
        var def = defs.FirstOrDefault(d => d.Key == key);
        Assert.NotNull(def);
        return def!;
    }
}
