[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$source = git -C $taskRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $source -notmatch '^[0-9a-f]{40}$') { throw 'Exact source revision is unavailable.' }
$version = (Get-Content -LiteralPath (Join-Path $taskRoot 'wails.json') -Raw | ConvertFrom-Json).info.productVersion
$testDirectory = Join-Path $taskRoot ('artifacts/release-tools-tests-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $testDirectory
try {
    $payload = Join-Path $testDirectory 'payload'
    & (Join-Path $PSScriptRoot 'New-ReleasePayload.ps1') -PublishDirectory (Join-Path $taskRoot 'build/bin') -OutputDirectory $payload -Version $version -SourceSha $source -Repository 'Just9120/llm-inspector' -SourceCreatedAt ([DateTimeOffset]'2026-09-05T00:00:00Z')
    & (Join-Path $PSScriptRoot 'Test-ReleasePayload.ps1') -PayloadDirectory $payload -Version $version -SourceSha $source
    $sbom = Get-Content -LiteralPath (Join-Path $payload "assets/LlmInspector-$version-win-x64.spdx.json") -Raw | ConvertFrom-Json
    $refs = @($sbom.packages | Where-Object { $_.PSObject.Properties.Name -contains 'externalRefs' } | ForEach-Object { $_.externalRefs.referenceLocator })
    if (-not ($refs -match '^pkg:golang/') -or -not ($refs -match '^pkg:npm/svelte@')) { throw 'Go/frontend SBOM coverage is missing.' }
    function Expect-Rejected([scriptblock] $Action) {
        $rejected = $false
        try { & $Action } catch { $rejected = $true }
        if (-not $rejected) { throw 'Release negative test unexpectedly succeeded.' }
    }
    $checksums = Join-Path $payload 'assets/SHA256SUMS.txt'
    $original = [IO.File]::ReadAllText($checksums)
    $line = (Get-Content -LiteralPath $checksums)[0]
    [IO.File]::WriteAllText($checksums, "$line`n$line`n$line`n")
    Expect-Rejected { & (Join-Path $PSScriptRoot 'Test-ReleasePayload.ps1') -PayloadDirectory $payload -Version $version -SourceSha $source }
    [IO.File]::WriteAllText($checksums, $original)
    Expect-Rejected { & (Join-Path $PSScriptRoot 'Test-ReleasePayload.ps1') -PayloadDirectory $payload -Version $version -SourceSha ('f' * 40) }
    [IO.File]::AppendAllText((Join-Path $payload "assets/LlmInspector-$version-win-x64.exe"), 'tamper-fixture')
    Expect-Rejected { & (Join-Path $PSScriptRoot 'Test-ReleasePayload.ps1') -PayloadDirectory $payload -Version $version -SourceSha $source }
    Write-Output 'Release tools PASS: exact Go/npm SBOM, payload identity, duplicate checksum/source/tamper rejection; no publication.'
}
finally {
    $resolved = [IO.Path]::GetFullPath($testDirectory)
    $allowedPrefix = [IO.Path]::GetFullPath((Join-Path $taskRoot 'artifacts/release-tools-tests-'))
    if (-not $resolved.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^release-tools-tests-[0-9a-f]{32}$') { throw 'Refusing unsafe test cleanup.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
