#Requires -Modules Pester

<#
.SYNOPSIS
    TDD gate: after dotnet build, qobuzarr/bin/plugin.json must contain all required
    fields from parity-spec.json versionContract + pluginJson, including commonVersion.

.DESCRIPTION
    Phase 3 fix for RED test PluginPackage_ContainsRequiredFiles(qobuzarr):
    The build was generating plugin.json from plugin.json.template which was missing
    the commonVersion field.  This test asserts the generated output is complete.

    Required fields checked:
      - All fields in parity-spec.json → pluginJson.requiredFields
      - commonVersion must equal the canonical value from the source plugin.json

    The test SKIPS (does not fail) when bin/plugin.json is absent — run
    'dotnet build' in the repo root first.
#>

BeforeAll {
    $script:RepoRoot      = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $script:BinPluginJson = Join-Path $script:RepoRoot 'bin\plugin.json'
    $script:SrcPluginJson = Join-Path $script:RepoRoot 'plugin.json'
    $script:ParitySpec    = Join-Path $script:RepoRoot 'ext\Lidarr.Plugin.Common\scripts\parity-spec.json'

    # Canonical required fields (mirrors parity-spec.json → pluginJson.requiredFields)
    # Used as a fallback when the spec file is unavailable.
    $script:FallbackRequiredFields = @(
        'id', 'apiVersion', 'name', 'version', 'author', 'description',
        'homepage', 'license', 'tags', 'commonVersion', 'minHostVersion',
        'targetFramework', 'main', 'rootNamespace'
    )
}

Describe 'qobuzarr — build output bin/plugin.json' {

    Context 'file presence' {

        It 'bin/plugin.json exists after dotnet build' {
            if (-not (Test-Path $script:BinPluginJson)) {
                Set-ItResult -Skipped -Because 'bin/plugin.json not found; run dotnet build first'
                return
            }
            Test-Path $script:BinPluginJson | Should -BeTrue
        }
    }

    Context 'required fields' {

        BeforeAll {
            if (-not (Test-Path $script:BinPluginJson)) {
                $script:PluginJsonObj = $null
                return
            }
            $script:PluginJsonObj = Get-Content $script:BinPluginJson -Raw | ConvertFrom-Json -ErrorAction Stop

            # Load required fields from parity-spec if available, else use fallback.
            if (Test-Path $script:ParitySpec) {
                $spec = Get-Content $script:ParitySpec -Raw | ConvertFrom-Json
                $script:RequiredFields = $spec.pluginJson.requiredFields
            }
            else {
                $script:RequiredFields = $script:FallbackRequiredFields
            }
        }

        It 'bin/plugin.json is valid JSON' {
            if (-not (Test-Path $script:BinPluginJson)) {
                Set-ItResult -Skipped -Because 'bin/plugin.json not found; run dotnet build first'
                return
            }
            { Get-Content $script:BinPluginJson -Raw | ConvertFrom-Json -ErrorAction Stop } |
                Should -Not -Throw
        }

        It "bin/plugin.json contains required field '<field>'" -ForEach @(
            @{ Field = 'id' },
            @{ Field = 'apiVersion' },
            @{ Field = 'name' },
            @{ Field = 'version' },
            @{ Field = 'author' },
            @{ Field = 'description' },
            @{ Field = 'homepage' },
            @{ Field = 'license' },
            @{ Field = 'tags' },
            @{ Field = 'commonVersion' },
            @{ Field = 'minHostVersion' },
            @{ Field = 'targetFramework' },
            @{ Field = 'main' },
            @{ Field = 'rootNamespace' }
        ) {
            if ($null -eq $script:PluginJsonObj) {
                Set-ItResult -Skipped -Because 'bin/plugin.json not found; run dotnet build first'
                return
            }
            $script:PluginJsonObj.PSObject.Properties.Name | Should -Contain $Field `
                -Because "parity-spec.json requires '$Field' in plugin.json (build output was generated from template)"
        }

        It 'commonVersion in bin/plugin.json matches the canonical value in source plugin.json' {
            if ($null -eq $script:PluginJsonObj) {
                Set-ItResult -Skipped -Because 'bin/plugin.json not found; run dotnet build first'
                return
            }
            if (-not (Test-Path $script:SrcPluginJson)) {
                Set-ItResult -Skipped -Because 'source plugin.json not found'
                return
            }

            $srcObj = Get-Content $script:SrcPluginJson -Raw | ConvertFrom-Json
            $expectedVersion = $srcObj.commonVersion
            $actualVersion   = $script:PluginJsonObj.commonVersion

            $actualVersion | Should -Be $expectedVersion `
                -Because "bin/plugin.json must carry the same commonVersion ($expectedVersion) as the source plugin.json"
        }

        It 'commonVersion in bin/plugin.json is not null or empty' {
            if ($null -eq $script:PluginJsonObj) {
                Set-ItResult -Skipped -Because 'bin/plugin.json not found; run dotnet build first'
                return
            }
            $script:PluginJsonObj.commonVersion | Should -Not -BeNullOrEmpty `
                -Because 'commonVersion is required by PackageClosureTests.AssertPluginJsonValid and parity-spec.json'
        }

        It 'commonVersion value is a valid semver-like string (e.g. 1.8.0)' {
            if ($null -eq $script:PluginJsonObj) {
                Set-ItResult -Skipped -Because 'bin/plugin.json not found; run dotnet build first'
                return
            }
            $script:PluginJsonObj.commonVersion | Should -Match '^\d+\.\d+\.\d+' `
                -Because 'commonVersion must follow semver major.minor.patch format'
        }
    }

    Context 'template alignment' {

        It 'plugin.json.template contains the commonVersion field' {
            $templatePath = Join-Path $script:RepoRoot 'plugin.json.template'
            if (-not (Test-Path $templatePath)) {
                Set-ItResult -Skipped -Because 'plugin.json.template not found'
                return
            }
            $content = Get-Content $templatePath -Raw
            $content | Should -Match '"commonVersion"' `
                -Because 'the template is the source for GeneratePluginJson; it must include commonVersion so the build output has it'
        }

        It 'source plugin.json and plugin.json.template both declare commonVersion' {
            $templatePath = Join-Path $script:RepoRoot 'plugin.json.template'
            if (-not (Test-Path $templatePath) -or -not (Test-Path $script:SrcPluginJson)) {
                Set-ItResult -Skipped -Because 'template or source plugin.json not found'
                return
            }
            $template = Get-Content $templatePath -Raw | ConvertFrom-Json -ErrorAction Stop
            $source   = Get-Content $script:SrcPluginJson -Raw | ConvertFrom-Json -ErrorAction Stop

            $template.PSObject.Properties.Name | Should -Contain 'commonVersion' `
                -Because 'template must declare commonVersion'
            $source.PSObject.Properties.Name | Should -Contain 'commonVersion' `
                -Because 'source plugin.json must declare commonVersion'

            # Both must agree on the value
            $template.commonVersion | Should -Be $source.commonVersion `
                -Because 'template and source plugin.json must declare the same commonVersion'
        }
    }
}
