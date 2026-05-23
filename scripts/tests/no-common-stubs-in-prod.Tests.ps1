#Requires -Modules Pester

<#
.SYNOPSIS
    Asserts that COMMON_STUBS is never set as an unconditional constant in production
    build files (Qobuzarr.csproj, Directory.Build.props, or any GitHub Actions workflow).

.DESCRIPTION
    src/Compat/CommonStubs.cs is an emergency-only fallback compiled only when
    $(UseCommonStubs) == 'true', which is triggered automatically when the
    Lidarr.Plugin.Common submodule is absent. Production CI always has the submodule
    present, so COMMON_STUBS must NEVER appear in a non-conditional, Release-targeting
    context.

    See: src/Compat/README.md for the full policy.
#>

BeforeAll {
    $script:RepoRoot = Resolve-Path (Join-Path $PSScriptRoot '..' '..')

    $script:CsprojPath   = Join-Path $script:RepoRoot 'Qobuzarr.csproj'
    $script:PropsPath    = Join-Path $script:RepoRoot 'Directory.Build.props'
    $script:WorkflowsDir = Join-Path $script:RepoRoot '.github' 'workflows'

    $script:CsprojContent  = if (Test-Path $script:CsprojPath)  { Get-Content $script:CsprojPath  -Raw } else { [string]::Empty }
    $script:PropsContent   = if (Test-Path $script:PropsPath)   { Get-Content $script:PropsPath   -Raw } else { [string]::Empty }

    $script:WorkflowFiles  = if (Test-Path $script:WorkflowsDir) {
        Get-ChildItem -Path $script:WorkflowsDir -Filter '*.yml' -Recurse
    } else { @() }

    $script:WorkflowContent = if ($script:WorkflowFiles.Count -gt 0) {
        ($script:WorkflowFiles | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
    } else { [string]::Empty }
}

Describe 'COMMON_STUBS — must never appear unconditionally in production build paths' {

    It 'Qobuzarr.csproj does not define COMMON_STUBS outside a Condition attribute' {
        # The csproj is allowed to define COMMON_STUBS only inside a Condition="..."
        # block (i.e., gated on $(UseCommonStubs) == 'true'). We count occurrences:
        # exactly 1 is the conditional DefineConstants block; >1 means an unconditional
        # definition was added, which is a policy violation.
        $occurrences = ([regex]::Matches($script:CsprojContent, 'COMMON_STUBS')).Count

        $occurrences | Should -BeLessOrEqual 1 `
            -Because ("COMMON_STUBS must appear at most once in Qobuzarr.csproj, " +
                     "and only inside a UseCommonStubs-gated Condition block. " +
                     "See src/Compat/README.md.")
    }

    It 'Qobuzarr.csproj COMMON_STUBS occurrence is inside a UseCommonStubs-guarded block' {
        $count = ([regex]::Matches($script:CsprojContent, 'COMMON_STUBS')).Count
        if ($count -eq 0) {
            Set-ItResult -Skipped -Because 'COMMON_STUBS not present in csproj (submodule build)'
            return
        }

        # The COMMON_STUBS definition must appear in a PropertyGroup that carries a
        # UseCommonStubs condition. We check that both strings appear within a 500-character
        # window of each other (they are consecutive lines in the csproj).
        $csIdx = $script:CsprojContent.IndexOf('COMMON_STUBS')
        $searchWindow = $script:CsprojContent.Substring([Math]::Max(0, $csIdx - 300), 400)
        $searchWindow | Should -Match 'UseCommonStubs' `
            -Because ("the COMMON_STUBS DefineConstants must be inside a " +
                     "UseCommonStubs-guarded PropertyGroup. See src/Compat/README.md.")
    }

    It 'Directory.Build.props does not define COMMON_STUBS' {
        $script:PropsContent | Should -Not -Match 'COMMON_STUBS' `
            -Because ("Directory.Build.props is applied to ALL projects in the repo. " +
                     "Defining COMMON_STUBS here would contaminate every build. " +
                     "See src/Compat/README.md.")
    }

    It 'No GitHub Actions workflow defines COMMON_STUBS' {
        $script:WorkflowContent | Should -Not -Match 'COMMON_STUBS' `
            -Because ("CI workflows must not set COMMON_STUBS; production CI always has " +
                     "the Lidarr.Plugin.Common submodule available. Stubs are " +
                     "emergency-only for external PRs without submodule access. " +
                     "See src/Compat/README.md.")
    }
}
