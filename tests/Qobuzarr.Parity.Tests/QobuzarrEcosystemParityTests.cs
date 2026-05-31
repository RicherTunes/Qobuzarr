using Lidarr.Plugin.Common.TestKit.Compliance;
using Xunit;
using System.Linq;

namespace Qobuzarr.Parity.Tests;

/// <summary>
/// Verifies Qobuzarr repo structure matches ecosystem parity standards.
/// Qobuzarr is the reference implementation — all tests should pass GREEN.
/// </summary>
[Trait("Category", "Parity")]
public class QobuzarrEcosystemParityTests : EcosystemParityTestBase
{
    protected override string RepoRootPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    protected override string PluginId => "qobuzarr";

    protected override string PluginJsonRelativePath => "plugin.json";

    /// <summary>
    /// Opt into wave 6 behavior-contract checks by exposing the compiled plugin assembly.
    /// </summary>
    protected override System.Reflection.Assembly? PluginAssembly =>
        typeof(Lidarr.Plugin.Qobuzarr.Integration.QobuzarrStreamingPlugin).Assembly;

    [Fact] public void DirectoryBuildProps_Exists_Test() => Assert.True(DirectoryBuildProps_Exists().Passed, string.Join("; ", DirectoryBuildProps_Exists().Errors));
    [Fact] public void DirectoryBuildProps_HasILRepackDisabled_Test() => Assert.True(DirectoryBuildProps_HasILRepackDisabled().Passed, string.Join("; ", DirectoryBuildProps_HasILRepackDisabled().Errors));
    [Fact] public void DirectoryBuildProps_HasVersionManagement_Test() => Assert.True(DirectoryBuildProps_HasVersionManagement().Passed, string.Join("; ", DirectoryBuildProps_HasVersionManagement().Errors));
    [Fact] public void DirectoryBuildProps_HasSourceLink_Test() => Assert.True(DirectoryBuildProps_HasSourceLink().Passed, string.Join("; ", DirectoryBuildProps_HasSourceLink().Errors));
    [Fact] public void DirectoryBuildProps_HasNoWarnSuppression_Test() => Assert.True(DirectoryBuildProps_HasNoWarnSuppression().Passed, string.Join("; ", DirectoryBuildProps_HasNoWarnSuppression().Errors));
    [Fact] public void DirectoryBuildProps_HasCPMExclusion_Test() => Assert.True(DirectoryBuildProps_HasCPMExclusion().Passed, string.Join("; ", DirectoryBuildProps_HasCPMExclusion().Errors));
    [Fact] public void DirectoryBuildProps_HasDeterministic_Test() => Assert.True(DirectoryBuildProps_HasDeterministic().Passed, string.Join("; ", DirectoryBuildProps_HasDeterministic().Errors));
    [Fact] public void DirectoryPackagesProps_Exists_Test() => Assert.True(DirectoryPackagesProps_Exists().Passed, string.Join("; ", DirectoryPackagesProps_Exists().Errors));
    [Fact] public void DirectoryPackagesProps_EnablesCPM_Test() => Assert.True(DirectoryPackagesProps_EnablesCPM().Passed, string.Join("; ", DirectoryPackagesProps_EnablesCPM().Errors));
    [Fact] public void DirectoryPackagesProps_HostVersionsMatchCanonical_Test() => Assert.True(DirectoryPackagesProps_HostVersionsMatchCanonical().Passed, string.Join("; ", DirectoryPackagesProps_HostVersionsMatchCanonical().Errors));
    [Fact] public void PluginJson_HasAllRequiredFields_Test() => Assert.True(PluginJson_HasAllRequiredFields().Passed, string.Join("; ", PluginJson_HasAllRequiredFields().Errors));
    [Fact] public void PluginJson_TargetFramework_IsNet8_Test() => Assert.True(PluginJson_TargetFramework_IsNet8().Passed, string.Join("; ", PluginJson_TargetFramework_IsNet8().Errors));
    [Fact] public void PluginJson_HasCommonVersion_Test() => Assert.True(PluginJson_HasCommonVersion().Passed, string.Join("; ", PluginJson_HasCommonVersion().Errors));
    [Fact] public void PluginJson_HasAuthor_Test() => Assert.True(PluginJson_HasAuthor().Passed, string.Join("; ", PluginJson_HasAuthor().Errors));
    [Fact] public void PluginJson_HasLicense_Test() => Assert.True(PluginJson_HasLicense().Passed, string.Join("; ", PluginJson_HasLicense().Errors));
    [Fact] public void PluginJson_HasTags_Test() => Assert.True(PluginJson_HasTags().Passed, string.Join("; ", PluginJson_HasTags().Errors));
    [Fact] public void PluginJson_HasRootNamespace_Test() => Assert.True(PluginJson_HasRootNamespace().Passed, string.Join("; ", PluginJson_HasRootNamespace().Errors));
    [Fact] public void PluginJson_NoNonStandardFields_Test() => Assert.True(PluginJson_NoNonStandardFields().Passed, string.Join("; ", PluginJson_NoNonStandardFields().Errors));
    [Fact] public void ManifestJson_TargetFramework_IsNet8_Test() => Assert.True(ManifestJson_TargetFramework_IsNet8().Passed, string.Join("; ", ManifestJson_TargetFramework_IsNet8().Errors));
    [Fact] public void GlobalJson_Exists_Test() => Assert.True(GlobalJson_Exists().Passed, string.Join("; ", GlobalJson_Exists().Errors));
    [Fact] public void GlobalJson_SdkVersion_Is8_0_100_Test() => Assert.True(GlobalJson_SdkVersion_Is8_0_100().Passed, string.Join("; ", GlobalJson_SdkVersion_Is8_0_100().Errors));

    // Behavior-contract checks (wave 6 — opt-in via PluginAssembly override above)
    //
    // Wave 11 refined the checks to eliminate the two prior false-positives:
    //  - Check_UsesCommonFileTokenStore now allowlists Lidarr.Plugin.Common.* namespace
    //    types (handles ILRepack-internalized MemoryTokenStore<T>).
    //  - Check_UsesCommonHttpResponseCache now walks the base-class chain to recognize
    //    legitimate subclasses of common's StreamingResponseCache (e.g. QobuzResponseCache).
    [Fact] public void Check_UsesCommonFileTokenStore_Test() => Assert.True(Check_UsesCommonFileTokenStore().Passed, string.Join("; ", Check_UsesCommonFileTokenStore().Errors));

    [Fact] public void Check_UsesCommonHttpResponseCache_Test() => Assert.True(Check_UsesCommonHttpResponseCache().Passed, string.Join("; ", Check_UsesCommonHttpResponseCache().Errors));

    [Fact] public void Check_RegistersBridgeDefaults_Test() => Assert.True(Check_RegistersBridgeDefaults().Passed, string.Join("; ", Check_RegistersBridgeDefaults().Errors));
    [Fact] public void Check_PluginManifest_Capabilities_HaveBackingTypes_Test() => Assert.True(Check_PluginManifest_Capabilities_HaveBackingTypes().Passed, string.Join("; ", Check_PluginManifest_Capabilities_HaveBackingTypes().Errors));
    [Fact] public void Check_NoFluentValidation_ErrorsApi_Drift_Test() => Assert.True(Check_NoFluentValidation_ErrorsApi_Drift().Passed, string.Join("; ", Check_NoFluentValidation_ErrorsApi_Drift().Errors));
    [Fact] public void Check_UsesCommonPluginConfigRoots_Test() => Assert.True(Check_UsesCommonPluginConfigRoots().Passed, string.Join("; ", Check_UsesCommonPluginConfigRoots().Errors));

    // Lyrics consolidation (PR #276): qobuz deleted its local LyricsEnricher/ILyricsEnricher and
    // now uses Common's shared ILyricsEnricher (inject-or-construct in TrackDownloadService). This
    // guard fails CI if a plugin-local lyrics type is ever re-introduced.
    [Fact] public void Check_UsesCommonLyricsEnricher_Test() => Assert.True(Check_UsesCommonLyricsEnricher().Passed, string.Join("; ", Check_UsesCommonLyricsEnricher().Errors));

    // Diagnostics consolidation (PR #277): qobuz deleted its local DiagnosticTypes/ErrorCodes nested
    // in *HealthDiagnostics and references Common's canonical Abstractions.Diagnostics types.
    [Fact] public void Check_UsesCommonDiagnosticTypes_Test() => Assert.True(Check_UsesCommonDiagnosticTypes().Passed, string.Join("; ", Check_UsesCommonDiagnosticTypes().Errors));
    // qobuz logs downloads ad-hoc (no plugin-local IDownloadTelemetrySink), so this passes — wired
    // for parity so a future plugin-local sink would fail CI.
    [Fact] public void Check_UsesCommonDownloadTelemetrySink_Test() => Assert.True(Check_UsesCommonDownloadTelemetrySink().Passed, string.Join("; ", Check_UsesCommonDownloadTelemetrySink().Errors));
    [Fact] public void Check_DownloadClientUsesPathTraversalGuard_Test() => Assert.True(Check_DownloadClientUsesPathTraversalGuard().Passed, string.Join("; ", Check_DownloadClientUsesPathTraversalGuard().Errors));
    [Fact] public void Check_FileClassNameParity_Test() => Assert.True(Check_FileClassNameParity().Passed, string.Join("; ", Check_FileClassNameParity().Errors));
    [Fact] public void Check_ClaudeMdDocumentsCommonHelpers_Test() => Assert.True(Check_ClaudeMdDocumentsCommonHelpers().Passed, string.Join("; ", Check_ClaudeMdDocumentsCommonHelpers().Errors));

    // Album-completion consolidation: qobuz's DownloadPolicy.IsAlbumDownloadSuccessful now delegates
    // to Common's AlbumCompletionPolicy, so an incomplete album reports Failed (Lidarr falls back)
    // instead of partial-Completed. This guard pins the shared rule and fails CI if the pinned Common
    // ever regresses it.
    [Fact] public void Check_EnforcesAlbumCompletionPolicy_Test() => Assert.True(Check_EnforcesAlbumCompletionPolicy().Passed, string.Join("; ", Check_EnforcesAlbumCompletionPolicy().Errors));
}
