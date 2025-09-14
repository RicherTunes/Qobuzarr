# Framework + SDK Policy

Goal: Build the plugin targeting `net6.0` (Lidarr compatibility) using the latest supported .NET SDK for reliability in CI and dev machines.

- Target frameworks
  - Plugin: `net6.0` (required for Lidarr 2.x plugin load)
  - CLI: `net6.0` for now; consider cross-targeting `net8.0` later

- SDK versioning
  - Pin SDK via `global.json` to `8.0.404` with `rollForward=latestMinor`
  - Rationale: newer SDKs can build older TFMs; aligns with TypNull/Tubifarry workflows that run .NET 8 SDK while building `net6.0`

- CI alignment (reference: TypNull/Tubifarry)
  - Use `actions/setup-dotnet` with SDK 8.x
  - Build with `dotnet build -f net6.0`
  - Install .NET 6 runtime for test execution if needed

- Package compatibility
  - Keep `SuppressTfmSupportBuildWarnings=true` during the transition to reduce noise when consuming net8 packages on net6 target; remove once packages are aligned

- Future
  - When Lidarr upgrades plugin TFM, bump plugin to `net8.0` and remove compatibility toggles

