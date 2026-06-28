#Requires -Version 5.0
<#
.SYNOPSIS
  Clean-build and run ChatGPT Wrapper from the repo-local .build-out folder.

.DESCRIPTION
  Stops any running instance, optionally clears .build-out, rebuilds Debug output to
  .build-out, and launches ChatGPT Wrapper.exe so you never accidentally run a stale build.

  Wrapper page assets (ChatGPT_files) are copied into .build-out\wrapper-assets on build.

.PARAMETER ExtendedDiagnostics
  Pass --extended-diagnostics to the app. Writes a comprehensive agent-oriented log at
  %LocalAppData%\ChatGPTWrapper\wrapper-diagnostics.jsonl (all channels + legacy mirrors).

.PARAMETER LogUiEvents
  Pass --log-ui-events to the app. Logs WPF shell events (tabs, arm state, pin reconcile).
  Implied when -ExtendedDiagnostics is set.

.PARAMETER SkipClean
  Skip deleting .build-out and dotnet clean. Faster iteration when only code changed.

.PARAMETER RemainingArgs
  Additional arguments forwarded to ChatGPT Wrapper.exe after diagnostic flags.

.EXAMPLE
  .\run.ps1

.EXAMPLE
  .\run.ps1 -ExtendedDiagnostics

.EXAMPLE
  .\run.ps1 -SkipClean -LogUiEvents
#>
param(
    [Alias('ExtendedDiagonstics')]
    [switch]$ExtendedDiagnostics,

    [switch]$LogUiEvents,

    [switch]$SkipClean,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArgs
)

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$project = Join-Path $repoRoot 'ChatGPTWrapper\ChatGPTWrapper.csproj'
$outDir = Join-Path $repoRoot '.build-out'
$exe = Join-Path $outDir 'ChatGPT Wrapper.exe'
$processName = 'ChatGPT Wrapper'
$logsDir = Join-Path $env:LOCALAPPDATA 'ChatGPTWrapper'

$running = Get-Process -Name $processName -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Stopping $($running.Count) running $processName instance(s)..."
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

if (-not $SkipClean) {
    Write-Host "Removing previous output: $outDir"
    if (Test-Path $outDir) {
        Remove-Item $outDir -Recurse -Force
    }

    Write-Host 'Cleaning project (bin/obj)...'
    dotnet clean $project -c Debug --verbosity minimal
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
else {
    Write-Host 'SkipClean: keeping existing .build-out and skipping dotnet clean.'
}

Write-Host "Building ChatGPT Wrapper -> $outDir"
dotnet build $project -c Debug -o $outDir --verbosity minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not (Test-Path -LiteralPath $exe)) {
    Write-Error "Build succeeded but executable was not found: $exe"
}

$launchArgs = [System.Collections.Generic.List[string]]::new()
if ($ExtendedDiagnostics) {
    $launchArgs.Add('--extended-diagnostics')
}
if ($LogUiEvents) {
    $launchArgs.Add('--log-ui-events')
}
if ($RemainingArgs) {
    $launchArgs.AddRange($RemainingArgs)
}

Write-Host "Starting $exe"
if ($launchArgs.Count -gt 0) {
    Write-Host "  Args: $($launchArgs -join ' ')"
}

if ($ExtendedDiagnostics) {
    Write-Host ""
    Write-Host "Extended diagnostics enabled. Logs:"
    Write-Host "  Unified:   $(Join-Path $logsDir 'wrapper-diagnostics.jsonl')"
    Write-Host "  Play send: $(Join-Path $logsDir 'play-send-trace.jsonl')"
    Write-Host "  Folder:    $logsDir"
    Write-Host ""
}
elseif ($LogUiEvents) {
    Write-Host ""
    Write-Host "UI event logging enabled -> $(Join-Path $logsDir 'wrapper-diagnostics.jsonl')"
    Write-Host ""
}

if ($launchArgs.Count -gt 0) {
    & $exe @launchArgs
}
else {
    & $exe
}
