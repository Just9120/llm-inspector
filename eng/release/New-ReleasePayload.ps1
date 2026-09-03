[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PublishDirectory,

    [Parameter(Mandatory)]
    [string] $AssetsFile,

    [Parameter(Mandatory)]
    [string] $OutputDirectory,

    [Parameter(Mandatory)]
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$')]
    [string] $Version,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string] $SourceSha,

    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string] $Repository,

    [Parameter(Mandatory)]
    [DateTimeOffset] $SourceCreatedAt
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedPublishDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
$resolvedAssetsFile = (Resolve-Path -LiteralPath $AssetsFile).Path
$resolvedOutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$publishedFiles = @(Get-ChildItem -LiteralPath $resolvedPublishDirectory -File)
if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne 'LlmInspector.App.exe') {
    $names = $publishedFiles.Name -join ', '
    throw "Expected exactly one published LlmInspector.App.exe; found: $names"
}

if (Test-Path -LiteralPath $resolvedOutputDirectory) {
    if (@(Get-ChildItem -LiteralPath $resolvedOutputDirectory -Force).Count -ne 0) {
        throw "Release payload directory must be empty: $resolvedOutputDirectory"
    }
}
else {
    $null = New-Item -ItemType Directory -Path $resolvedOutputDirectory
}

$assetsDirectory = Join-Path $resolvedOutputDirectory 'assets'
$null = New-Item -ItemType Directory -Path $assetsDirectory
$artifactName = "LlmInspector-$Version-win-x64.exe"
$artifactPath = Join-Path $assetsDirectory $artifactName
Copy-Item -LiteralPath $publishedFiles[0].FullName -Destination $artifactPath
$artifactInfo = Get-Item -LiteralPath $artifactPath
$artifactHash = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
$normalizedSha = $SourceSha.ToLowerInvariant()
$created = $SourceCreatedAt.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')

$projectAssets = Get-Content -LiteralPath $resolvedAssetsFile -Raw | ConvertFrom-Json
$packageProperties = @($projectAssets.libraries.PSObject.Properties |
    Where-Object { $_.Value.type -eq 'package' } |
    Sort-Object Name)
$packages = [Collections.Generic.List[object]]::new()
$relationships = [Collections.Generic.List[object]]::new()
$relationships.Add([ordered]@{
    spdxElementId = 'SPDXRef-DOCUMENT'
    relationshipType = 'DESCRIBES'
    relatedSpdxElement = 'SPDXRef-Package-LlmInspector-App'
})
$relationships.Add([ordered]@{
    spdxElementId = 'SPDXRef-Package-LlmInspector-App'
    relationshipType = 'CONTAINS'
    relatedSpdxElement = 'SPDXRef-File-LlmInspector-App'
})

$packages.Add([ordered]@{
    name = 'LlmInspector.App'
    SPDXID = 'SPDXRef-Package-LlmInspector-App'
    versionInfo = $Version
    downloadLocation = 'NOASSERTION'
    filesAnalyzed = $true
    checksums = @([ordered]@{ algorithm = 'SHA256'; checksumValue = $artifactHash })
    licenseConcluded = 'NOASSERTION'
    licenseDeclared = 'NOASSERTION'
    copyrightText = 'NOASSERTION'
})

for ($index = 0; $index -lt $packageProperties.Count; $index++) {
    $packageKey = $packageProperties[$index].Name
    $separator = $packageKey.LastIndexOf('/')
    if ($separator -le 0 -or $separator -eq $packageKey.Length - 1) {
        throw "Invalid NuGet package identity in project.assets.json: $packageKey"
    }

    $packageName = $packageKey.Substring(0, $separator)
    $packageVersion = $packageKey.Substring($separator + 1)
    $spdxId = "SPDXRef-NuGet-$($index + 1)"
    $packageUrl = "pkg:nuget/$([Uri]::EscapeDataString($packageName))@$([Uri]::EscapeDataString($packageVersion))"
    $packages.Add([ordered]@{
        name = $packageName
        SPDXID = $spdxId
        versionInfo = $packageVersion
        downloadLocation = "https://www.nuget.org/packages/$packageName/$packageVersion"
        filesAnalyzed = $false
        licenseConcluded = 'NOASSERTION'
        licenseDeclared = 'NOASSERTION'
        copyrightText = 'NOASSERTION'
        externalRefs = @([ordered]@{
            referenceCategory = 'PACKAGE-MANAGER'
            referenceType = 'purl'
            referenceLocator = $packageUrl
        })
    })
    $relationships.Add([ordered]@{
        spdxElementId = 'SPDXRef-Package-LlmInspector-App'
        relationshipType = 'DEPENDS_ON'
        relatedSpdxElement = $spdxId
    })
}

$sbomName = "LlmInspector-$Version-win-x64.spdx.json"
$sbomPath = Join-Path $assetsDirectory $sbomName
$sbom = [ordered]@{
    spdxVersion = 'SPDX-2.3'
    dataLicense = 'CC0-1.0'
    SPDXID = 'SPDXRef-DOCUMENT'
    name = "LlmInspector-$Version-win-x64"
    documentNamespace = "https://github.com/$Repository/releases/download/v$Version/spdx/$normalizedSha"
    creationInfo = [ordered]@{
        created = $created
        creators = @('Tool: LlmInspector.New-ReleasePayload/1.0')
    }
    packages = $packages
    files = @([ordered]@{
        fileName = "./$artifactName"
        SPDXID = 'SPDXRef-File-LlmInspector-App'
        checksums = @([ordered]@{ algorithm = 'SHA256'; checksumValue = $artifactHash })
        licenseConcluded = 'NOASSERTION'
        copyrightText = 'NOASSERTION'
    })
    relationships = $relationships
}
$utf8NoBom = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText(
    $sbomPath,
    (($sbom | ConvertTo-Json -Depth 20) -replace "`r`n", "`n") + "`n",
    $utf8NoBom)

$manifestName = "LlmInspector-$Version-win-x64.release.json"
$manifestPath = Join-Path $assetsDirectory $manifestName
$manifest = [ordered]@{
    schema_version = 'portable-release-v1'
    version = $Version
    source_repository = "https://github.com/$Repository"
    source_sha = $normalizedSha
    source_created_at = $created
    artifact = [ordered]@{
        name = $artifactName
        sha256 = $artifactHash
        bytes = $artifactInfo.Length
        runtime_identifier = 'win-x64'
        self_contained = $true
        single_file = $true
        signed = $false
    }
    sbom = $sbomName
    provenance = 'GitHub artifact attestation (Sigstore/in-toto SLSA provenance)'
    support_matrix = @('Windows 11 25H2 Home x64', 'Windows 11 25H2 Pro x64')
}
[IO.File]::WriteAllText(
    $manifestPath,
    (($manifest | ConvertTo-Json -Depth 10) -replace "`r`n", "`n") + "`n",
    $utf8NoBom)

$checksumSubjects = @($artifactPath, $sbomPath, $manifestPath) |
    ForEach-Object {
        $name = [IO.Path]::GetFileName($_)
        $hash = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash *$name"
    } |
    Sort-Object
[IO.File]::WriteAllText(
    (Join-Path $assetsDirectory 'SHA256SUMS.txt'),
    ($checksumSubjects -join "`n") + "`n",
    $utf8NoBom)

$releaseNotes = @"
# LLM Inspector v$Version

Observation-only portable preview for Windows 11 25H2 x64. Lifecycle management is not included in v1.0.

## Запуск

1. Скачайте $artifactName и SHA256SUMS.txt.
2. Проверьте SHA-256 командой Get-FileHash .\$artifactName -Algorithm SHA256.
3. Запустите executable без installer и прав администратора.

Пакет self-contained: установленный .NET SDK/runtime не требуется. Application data остаются в `%LOCALAPPDATA%\LLM Inspector`.

## Важное предупреждение

Executable пока не подписан доверенным code-signing certificate. Microsoft Defender SmartScreen может показать предупреждение для нового файла. Продолжайте запуск только если файл скачан из этого GitHub Release и его SHA-256 совпадает с `SHA256SUMS.txt`. Automatic update отсутствует; новые версии устанавливаются только явной ручной загрузкой.

Source commit: $normalizedSha.
SBOM: $sbomName.
Build provenance и SBOM attestation опубликованы как Sigstore bundles и в GitHub artifact attestations.
"@
[IO.File]::WriteAllText(
    (Join-Path $resolvedOutputDirectory 'release-notes.md'),
    ($releaseNotes.TrimEnd() -replace "`r`n", "`n") + "`n",
    $utf8NoBom)

Write-Output ([ordered]@{
    artifact = $artifactName
    sha256 = $artifactHash
    sbom = $sbomName
    manifest = $manifestName
    package_count = $packageProperties.Count
} | ConvertTo-Json -Compress)
