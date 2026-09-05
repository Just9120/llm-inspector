[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$env:GOTOOLCHAIN = 'local'
$env:GOFLAGS = '-mod=readonly'
$expectedGoVersion = (Get-Content -LiteralPath (Join-Path $PSScriptRoot '../.go-version') -Raw).Trim()
$actualGoVersion = go env GOVERSION
if ($LASTEXITCODE -ne 0 -or $actualGoVersion -ne "go$expectedGoVersion") {
    throw "Expected pinned Go $expectedGoVersion."
}
$goSources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '../internal') -Filter '*.go' -Recurse -File | Select-Object -ExpandProperty FullName)
$goSources += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '..') -Filter '*.go' -File | Select-Object -ExpandProperty FullName)
$goSources += @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.go' -File | Select-Object -ExpandProperty FullName)
$unformatted = @(gofmt -l $goSources)
if ($LASTEXITCODE -ne 0 -or $unformatted.Count -gt 0) {
    throw 'Go formatting verification failed. Run gofmt on changed files.'
}
go mod verify
if ($LASTEXITCODE -ne 0) { throw 'Go module verification failed.' }
go vet ./internal/...
if ($LASTEXITCODE -ne 0) { throw 'Go vet failed.' }
go test ./internal/... -count=1 -timeout 60s -cover
if ($LASTEXITCODE -ne 0) { throw 'Go tests failed.' }
go build ./internal/...
if ($LASTEXITCODE -ne 0) { throw 'Go build failed.' }
