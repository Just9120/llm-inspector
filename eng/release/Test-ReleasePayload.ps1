[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PayloadDirectory,

    [Parameter(Mandatory)]
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$')]
    [string] $Version,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string] $SourceSha
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedPayloadDirectory = (Resolve-Path -LiteralPath $PayloadDirectory).Path
$assetsDirectory = Join-Path $resolvedPayloadDirectory 'assets'
$artifactName = "LlmInspector-$Version-win-x64.exe"
$sbomName = "LlmInspector-$Version-win-x64.spdx.json"
$manifestName = "LlmInspector-$Version-win-x64.release.json"
$expectedAssets = @($artifactName, $manifestName, $sbomName, 'SHA256SUMS.txt') | Sort-Object
$actualAssets = @(Get-ChildItem -LiteralPath $assetsDirectory -File).Name | Sort-Object
if (Compare-Object -ReferenceObject $expectedAssets -DifferenceObject $actualAssets) {
    throw "Release payload contains an unexpected asset set: $($actualAssets -join ', ')"
}

$notesPath = Join-Path $resolvedPayloadDirectory 'release-notes.md'
if (-not (Test-Path -LiteralPath $notesPath -PathType Leaf)) {
    throw 'Release notes are missing.'
}

$checksumsPath = Join-Path $assetsDirectory 'SHA256SUMS.txt'
$checksumLines = @(Get-Content -LiteralPath $checksumsPath)
if ($checksumLines.Count -ne 3) {
    throw 'SHA256SUMS.txt must identify exactly the executable, manifest and SBOM.'
}

foreach ($line in $checksumLines) {
    if ($line -notmatch '^(?<hash>[0-9a-f]{64}) \*(?<name>[A-Za-z0-9_.-]+)$') {
        throw "Invalid checksum entry: $line"
    }

    $subjectPath = Join-Path $assetsDirectory $Matches.name
    if (-not (Test-Path -LiteralPath $subjectPath -PathType Leaf)) {
        throw "Checksum subject is missing: $($Matches.name)"
    }

    $actualHash = (Get-FileHash -LiteralPath $subjectPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $Matches.hash) {
        throw "Checksum mismatch: $($Matches.name)"
    }
}

$manifest = Get-Content -LiteralPath (Join-Path $assetsDirectory $manifestName) -Raw | ConvertFrom-Json
if ($manifest.schema_version -ne 'portable-release-v1' -or
    $manifest.version -ne $Version -or
    $manifest.source_sha -ne $SourceSha.ToLowerInvariant() -or
    $manifest.artifact.name -ne $artifactName -or
    $manifest.artifact.signed -ne $false -or
    $manifest.artifact.self_contained -ne $true -or
    $manifest.artifact.single_file -ne $true) {
    throw 'Release manifest identity or packaging contract is invalid.'
}

$artifactHash = (Get-FileHash -LiteralPath (Join-Path $assetsDirectory $artifactName) -Algorithm SHA256).Hash.ToLowerInvariant()
if ($manifest.artifact.sha256 -ne $artifactHash) {
    throw 'Release manifest executable digest does not match the payload.'
}

$sbom = Get-Content -LiteralPath (Join-Path $assetsDirectory $sbomName) -Raw | ConvertFrom-Json
if ($sbom.spdxVersion -ne 'SPDX-2.3' -or
    $sbom.dataLicense -ne 'CC0-1.0' -or
    $sbom.SPDXID -ne 'SPDXRef-DOCUMENT' -or
    @($sbom.packages).Count -lt 2 -or
    @($sbom.files).Count -ne 1) {
    throw 'SPDX SBOM structure is incomplete.'
}

$notes = Get-Content -LiteralPath $notesPath -Raw
if ($notes -notmatch [Regex]::Escape($SourceSha.ToLowerInvariant()) -or
    $notes -notmatch 'SmartScreen' -or
    $notes -notmatch 'не подписан') {
    throw 'Release notes do not contain source identity and unsigned-package guidance.'
}

Write-Output "Verified portable release payload for v$Version ($artifactHash)."
