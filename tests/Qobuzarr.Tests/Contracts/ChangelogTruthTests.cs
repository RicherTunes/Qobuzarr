using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Qobuzarr.Tests.Contracts;

/// <summary>
/// Documentation-truth guard (external-audit finding Q-6, 2026-07-02): CHANGELOG.md must not
/// silently drift from the pinned Common version. <see cref="VersionContractTests"/> already
/// cross-checks plugin.json's <c>commonVersion</c> against the submodule's actual
/// <c>Directory.Build.props</c> (and <c>Qobuzarr.Parity.Tests.CommonPinDriftTests</c> cross-checks
/// <c>ext-common-sha.txt</c> against the checked-out submodule HEAD) — but nothing forced
/// CHANGELOG.md itself, which is documentation rather than code, to track the pin. Before this
/// test was added, CHANGELOG's <c>[Unreleased]</c> section (top of file — "newest" both by
/// Keep-a-Changelog convention and by this repo's own newest-first ordering within a section)
/// declared <c>commonVersion: 1.16.0 -&gt; 1.17.0</c> while plugin.json had already moved to
/// <c>1.18.0-dev</c> many commits earlier: the changelog was effectively frozen around
/// 2026-05-29 and never caught back up despite dozens of subsequent Common re-pins.
///
/// <para>
/// <b>Parse approach (documented tradeoff):</b> scan CHANGELOG.md top-to-bottom for the first
/// line containing the literal substring "commonVersion" (case-insensitive); on that line, take
/// the LAST dotted-version-looking token. Every existing "commonVersion" line in this file is
/// written as "X.Y.Z -&gt; A.B.C" (arrow), so the last token is the target/new value, and a line
/// with only one token is trivially "the value". "First matching line from the top" is treated
/// as "newest" because this repo's history consistently orders both sections (release headers)
/// and bullets within a section newest-first (visible e.g. in the historical Dependencies list
/// under <c>[0.5.10]</c>, where the most recent re-pin is listed first). This is a heuristic, not
/// a full changelog/markdown parser — it will mis-fire if a future edit ever prepends an OLDER
/// commonVersion mention above a newer one out of chronological order. That tradeoff is accepted
/// deliberately: a full date/section-aware parser is a lot more machinery for a low-stakes
/// staleness check, a false RED here is cheap to diagnose (reorder/reword the offending line),
/// and the guard's entire job — making commonVersion drift visible instead of silently stale —
/// holds either way.
/// </para>
/// </summary>
public class ChangelogTruthTests
{
    private static readonly Regex VersionToken =
        new(@"\d+\.\d+\.\d+(?:-[A-Za-z0-9]+)?", RegexOptions.Compiled);

    [Fact]
    public void Changelog_NewestCommonVersionMention_MatchesPluginJson()
    {
        var pluginJsonPath = LocateRepoFile("plugin.json");
        var changelogPath = LocateRepoFile("CHANGELOG.md");
        Skip.If(pluginJsonPath is null || changelogPath is null,
            "plugin.json or CHANGELOG.md not found — only enforced for repo-rooted runs");

        using var doc = JsonDocument.Parse(File.ReadAllText(pluginJsonPath!));
        var pluginCommonVersion = doc.RootElement.GetProperty("commonVersion").GetString();
        Assert.False(string.IsNullOrWhiteSpace(pluginCommonVersion), "plugin.json must declare commonVersion");

        var newestMention = FindNewestCommonVersionMention(changelogPath!);
        Assert.False(newestMention is null,
            "CHANGELOG.md has no line mentioning 'commonVersion' with a parseable version token — " +
            "add one (e.g. in the newest [Unreleased]/Dependencies entry) so this guard can check it.");

        Assert.Equal(pluginCommonVersion, newestMention);
    }

    /// <summary>
    /// Returns the last version-shaped token on the first "commonVersion"-mentioning line,
    /// scanning top-to-bottom. Returns null if no such line exists.
    /// </summary>
    private static string? FindNewestCommonVersionMention(string changelogPath)
    {
        foreach (var line in File.ReadLines(changelogPath))
        {
            if (line.IndexOf("commonVersion", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            var matches = VersionToken.Matches(line);
            if (matches.Count == 0)
            {
                continue;
            }

            return matches[matches.Count - 1].Value;
        }

        return null;
    }

    private static string? LocateRepoFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
