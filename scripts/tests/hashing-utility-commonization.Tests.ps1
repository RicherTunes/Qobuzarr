#Requires -Modules Pester

<#
.SYNOPSIS
    Phase 0.8 — proof-of-process commonization for Qobuzarr.Utilities.HashingUtility.

.DESCRIPTION
    qobuzarr/src/Utilities/HashingUtility.cs is a thin wrapper around
    Lidarr.Plugin.Common.Utilities.HashingUtility. Two of its three methods are
    pure pass-throughs. This test pins:

      1. The wrapper class still exists (no surprise removal — callers depend on it)
      2. The pass-through methods (ComputeMD5Hash, GenerateCacheKey) are marked
         [Obsolete] with a removal target, directing callers to Common.
      3. ComputePasswordMD5Hash is NOT obsolete — it adds plugin-specific
         password validation that does not belong in Common.

    The reviewer's recommended migration shape:
      • Wrappers MUST carry [Obsolete] with removal version once Common has
        equivalent coverage; otherwise the shim becomes permanent debt.
      • Common's HashingUtility already has characterization tests in
        tests/Utilities/HashingUtilityTests.cs (ComputeMD5Hash, GenerateCacheKey,
        ComputeSHA256, ComputeHmacSha256) — re-verified 2026-05-23.
#>

BeforeAll {
    $script:HashingUtilityPath = Join-Path $PSScriptRoot '..' '..' 'src' 'Utilities' 'HashingUtility.cs'
    $script:HashingUtilityPath = Resolve-Path $script:HashingUtilityPath
    $script:Source = Get-Content $script:HashingUtilityPath -Raw
}

Describe 'qobuzarr/src/Utilities/HashingUtility.cs — commonization markers' {

    It 'file still exists (wrapper preserved during migration)' {
        Test-Path $script:HashingUtilityPath | Should -BeTrue
    }

    It 'ComputeMD5Hash carries [Obsolete] directing callers to Common' {
        # Look for an Obsolete attribute immediately preceding ComputeMD5Hash.
        $script:Source | Should -Match '(?s)\[Obsolete\([^)]*Lidarr\.Plugin\.Common\.Utilities\.HashingUtility[^)]*\)\]\s*public\s+static\s+string\s+ComputeMD5Hash'
    }

    It 'GenerateCacheKey carries [Obsolete] directing callers to Common' {
        $script:Source | Should -Match '(?s)\[Obsolete\([^)]*Lidarr\.Plugin\.Common\.Utilities\.HashingUtility[^)]*\)\]\s*public\s+static\s+string\s+GenerateCacheKey'
    }

    It 'ComputePasswordMD5Hash is NOT marked obsolete (plugin-specific validation)' {
        $script:Source | Should -Not -Match '(?s)\[Obsolete\([^)]*\)\]\s*public\s+static\s+string\s+ComputePasswordMD5Hash'
    }

    It 'Obsolete markers cite a removal version' {
        # Removal target should appear in the message so the shim has a deadline.
        $script:Source | Should -Match 'remove(d)?\s+in\s+(qobuzarr\s+)?v0\.2\.0|Removal:\s*v0\.2\.0'
    }

    It 'ComputePasswordMD5Hash calls Common directly (no self-obsolete CS0618 chain)' {
        # Adversarial review blocker: if ComputePasswordMD5Hash calls the local (now-obsolete)
        # ComputeMD5Hash, the compiler emits CS0618 inside a non-obsolete method on every build.
        # Fix: the body must call Lidarr.Plugin.Common.Utilities.HashingUtility.ComputeMD5Hash directly.
        $body = if ($script:Source -match '(?s)public\s+static\s+string\s+ComputePasswordMD5Hash[^{]*\{(.*?)\n\s*\}') { $matches[1] } else { '' }
        $body | Should -Not -Be ''
        $body | Should -Not -Match '\breturn\s+ComputeMD5Hash\s*\('
        $body | Should -Match 'Lidarr\.Plugin\.Common\.Utilities\.HashingUtility\.ComputeMD5Hash'
    }
}
