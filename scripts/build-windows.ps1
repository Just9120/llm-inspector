[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$taskRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (-not $IsWindows) { throw 'This product build requires Windows x64.' }
Push-Location -LiteralPath $taskRoot
$previousGoBin = $env:GOBIN
try {
    ./scripts/validate-go.ps1
    $nodeVersion = (Get-Content -LiteralPath .node-version -Raw).Trim()
    $npmVersion = (Get-Content -LiteralPath .npm-version -Raw).Trim()
    if ((node --version) -ne "v$nodeVersion" -or $LASTEXITCODE -ne 0) { throw "Expected Node $nodeVersion." }
    if ((npm --version) -ne $npmVersion -or $LASTEXITCODE -ne 0) { throw "Expected npm $npmVersion." }
    # Wails' checksum cache is not a locked install guarantee after interruption.
    npm --prefix frontend ci --ignore-scripts --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw 'Locked frontend install failed. Stop this checkout preview before retrying.' }
    $env:GOBIN = Join-Path $taskRoot 'artifacts/tools'
    $null = New-Item -ItemType Directory -Path $env:GOBIN -Force
    go install github.com/wailsapp/wails/v2/cmd/wails@v2.15.0
    if ($LASTEXITCODE -ne 0) { throw 'Pinned Wails tool install failed.' }
    # Wails creates absent embed directories before binding generation, then
    # runs frontend check/test/build; generated JS is never committed.
    & (Join-Path $env:GOBIN 'wails.exe') build -webview2 error -platform windows/amd64 -o LlmInspector.exe -trimpath -nocolour
    if ($LASTEXITCODE -ne 0) { throw 'Windows executable build failed.' }
    go mod verify
    if ($LASTEXITCODE -ne 0) { throw 'Module integrity failed after build.' }
    go vet .
    if ($LASTEXITCODE -ne 0) { throw 'Windows host vet failed.' }
    go test . -count=1 -timeout 60s
    if ($LASTEXITCODE -ne 0) { throw 'Windows host tests failed.' }
    ./scripts/smoke-windows.ps1
    Get-FileHash -LiteralPath ./build/bin/LlmInspector.exe -Algorithm SHA256
}
finally {
    $env:GOBIN = $previousGoBin
    Pop-Location
}
