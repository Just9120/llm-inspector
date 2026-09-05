[CmdletBinding()]
param([string] $Executable = (Join-Path $PSScriptRoot '../build/bin/LlmInspector.exe'))

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$resolvedExecutable = (Resolve-Path -LiteralPath $Executable).Path
$taskLogs = Join-Path $PSScriptRoot '../artifacts'
$null = New-Item -ItemType Directory -Path $taskLogs -Force
$stdout = Join-Path $taskLogs 'go-desktop-smoke.stdout.log'
$stderr = Join-Path $taskLogs 'go-desktop-smoke.stderr.log'
# & can return before a GUI executable exits. Require the actual child exit.
$child = Start-Process -FilePath $resolvedExecutable -ArgumentList '--smoke-test' -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
try {
    if (-not $child.WaitForExit(55000)) {
        $child.Kill()
        $child.WaitForExit(5000) | Out-Null
        throw 'Native Windows smoke timed out; only its own child was terminated.'
    }
    $child.WaitForExit()
    $text = Get-Content -LiteralPath $stdout -Raw
    Get-Content -LiteralPath $stderr
    if ($child.ExitCode -ne 0 -or $text -notmatch 'Go desktop smoke: PASS') { throw "Native Windows smoke failed (exit $($child.ExitCode))." }
    Write-Output $text.Trim()
}
finally { $child.Dispose() }
