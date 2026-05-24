# Versioning — Qobuzarr

## Source of truth: `VERSION` file

The single source of truth for the plugin version is the top-level `VERSION` file
(e.g. `0.5.3`).  All other version references are derived from it automatically.

| Artifact | How it gets the version | Do not edit manually |
|---|---|---|
| `VERSION` | **Source of truth** — edit this one | — |
| Assembly `InformationalVersion` | `Qobuzarr.csproj` reads `VERSION` via `$([System.IO.File]::ReadAllText('VERSION').Trim())` | yes |
| `plugin.json` `.version` | Generated at build time from `plugin.json.template` (replaces `{VERSION}`) | yes — edit template |

## Wiring (clean pattern — reference for other plugins)

`Qobuzarr.csproj` contains:

```xml
<VersionFromFile Condition="'$(VersionFromFile)' == '' And Exists('VERSION')">
  $([System.IO.File]::ReadAllText('VERSION').Trim())
</VersionFromFile>
<Version Condition="'$(Version)' == '' And '$(VersionFromFile)' != ''">$(VersionFromFile)</Version>
```

And a `GeneratePluginJson` MSBuild target reads `plugin.json.template`, replaces
`{VERSION}` with `$(AssemblyInformationalVersion)`, and writes `plugin.json` to the
intermediate output directory.  The release workflow picks up the generated file —
no manual sync needed.

## Bumping a version

1. Edit `VERSION` with the new semver string.
2. Push a git tag `v<VERSION>`.
3. Everything else is automatic.
