[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PublishDirectory,

    [string] $PackageLockFile = (Join-Path $PSScriptRoot '../../frontend/package-lock.json'),

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
$resolvedLockFile = (Resolve-Path -LiteralPath $PackageLockFile).Path
$resolvedOutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$publishedFiles = @(Get-ChildItem -LiteralPath $resolvedPublishDirectory -File)
if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne 'LlmInspector.exe') {
    $names = $publishedFiles.Name -join ', '
    throw "Expected exactly one published LlmInspector.exe; found: $names"
}

$buildInfoText = go version -m -json $publishedFiles[0].FullName
if ($LASTEXITCODE -ne 0) { throw 'Cannot read exact executable Go build information.' }
$buildInfo = ($buildInfoText -join "`n") | ConvertFrom-Json
if ($buildInfo.Path -ne 'github.com/Just9120/llm-inspector' -or $buildInfo.GoVersion -notmatch '^go\d+\.\d+\.\d+$') {
    throw 'Executable Go module/toolchain identity is invalid.'
}
$packageLock = Get-Content -LiteralPath $resolvedLockFile -Raw | ConvertFrom-Json -AsHashtable
if ($packageLock.lockfileVersion -ne 3 -or $packageLock.version -ne $Version) { throw 'Frontend lockfile/version identity is invalid.' }

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

$dependencies = [Collections.Generic.List[object]]::new()
foreach ($module in @($buildInfo.Deps | Sort-Object Path)) {
    if ($module.PSObject.Properties.Name -contains 'Replace' -or $module.Version -notmatch '^v[0-9]' -or $module.Sum -notmatch '^h1:') {
        throw 'Unreviewed replacement or unverified Go module in executable.'
    }
    $dependencies.Add(@{ name=$module.Path; version=$module.Version; ecosystem='golang'; relation='DEPENDS_ON'; location='NOASSERTION'; comment="Exact executable module, Go checksum $($module.Sum)" })
}
foreach ($property in @($packageLock.packages.GetEnumerator() | Where-Object Key -ne '' | Sort-Object Key)) {
    $entry = $property.Value
    if ($property.Key -notmatch '(^|/)node_modules/(?<package>(@[^/]+/)?[^/]+)$') { throw 'Invalid npm package path.' }
    $name = $Matches.package
    if ($entry.version -notmatch '^\d+\.' -or $entry.integrity -notmatch '^sha512-') {
        throw 'Invalid npm lockfile package identity/integrity.'
    }
    $relation = if ($name -eq 'svelte') { 'DEPENDS_ON' } else { 'BUILD_DEPENDENCY_OF' }
    $dependencies.Add(@{ name=$name; version=$entry.version; ecosystem='npm'; relation=$relation; location=$entry.resolved; comment="Locked frontend build graph; integrity $($entry.integrity)" })
}
$dependencies.Add(@{ name='go'; version=$buildInfo.GoVersion.Substring(2); ecosystem='generic'; relation='DEPENDS_ON'; location='https://go.dev/dl/'; comment='Go compiler and linked standard-library runtime' })
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
    filesAnalyzed = $false
    checksums = @([ordered]@{ algorithm = 'SHA256'; checksumValue = $artifactHash })
    licenseConcluded = 'NOASSERTION'
    licenseDeclared = 'NOASSERTION'
    copyrightText = 'NOASSERTION'
})

for ($index = 0; $index -lt $dependencies.Count; $index++) {
    $dependency = $dependencies[$index]
    $packageName = $dependency.name
    $packageVersion = $dependency.version
    $spdxId = "SPDXRef-Dependency-$($index + 1)"
    $encodedName = [Uri]::EscapeDataString($packageName).Replace('%2F', '/')
    $packageUrl = "pkg:$($dependency.ecosystem)/$encodedName@$([Uri]::EscapeDataString($packageVersion))"
    $packages.Add([ordered]@{
        name = $packageName
        SPDXID = $spdxId
        versionInfo = $packageVersion
        downloadLocation = $dependency.location
        filesAnalyzed = $false
        licenseConcluded = 'NOASSERTION'
        licenseDeclared = 'NOASSERTION'
        copyrightText = 'NOASSERTION'
        comment = $dependency.comment
        externalRefs = @([ordered]@{
            referenceCategory = 'PACKAGE-MANAGER'
            referenceType = 'purl'
            referenceLocator = $packageUrl
        })
    })
    $relationships.Add([ordered]@{
        spdxElementId = $(if ($dependency.relation -eq 'BUILD_DEPENDENCY_OF') { $spdxId } else { 'SPDXRef-Package-LlmInspector-App' })
        relationshipType = $dependency.relation
        relatedSpdxElement = $(if ($dependency.relation -eq 'BUILD_DEPENDENCY_OF') { 'SPDXRef-Package-LlmInspector-App' } else { $spdxId })
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
        runtime_prerequisite = 'Microsoft Edge WebView2 Evergreen Runtime; installed separately, never auto-downloaded by Inspector'
    }
    sbom = $sbomName
    frontend_lock_sha256 = (Get-FileHash -LiteralPath $resolvedLockFile -Algorithm SHA256).Hash.ToLowerInvariant()
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

Русскоязычное portable приложение для технического наблюдения за LLM на Windows 11 25H2 x64. Управление supported backend включается только после явного подтверждения точного runtime.

## Запуск

1. Скачайте $artifactName и SHA256SUMS.txt.
2. Проверьте SHA-256 командой Get-FileHash .\$artifactName -Algorithm SHA256.
3. Запустите executable без installer и прав администратора.

Go runtime и frontend встроены: .NET, Node.js и Go на компьютере пользователя не нужны. Требуется установленный Microsoft Edge WebView2 Evergreen Runtime. Программа не скачивает и не устанавливает его автоматически. Application data остаются в `%LOCALAPPDATA%\LLM Inspector`.

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
    package_count = $dependencies.Count
} | ConvertTo-Json -Compress)
