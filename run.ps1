#Requires -Version 5.0
<#
.SYNOPSIS
  Clean-build and run ChatGPT Wrapper from the repo-local .build-out folder.

.DESCRIPTION
  Stops any running instance, optionally clears .build-out, rebuilds Debug output to
  .build-out, and launches the WinUI host executable so you never accidentally run a stale build.

  WinUI is the sole executable host (CMD-517). The WPF project builds as a dialog/domain
  library (ChatGPT Wrapper.dll) and is pulled in transitively by the WinUI project build.

  Wrapper page assets (ChatGPT_files) are copied into .build-out\wrapper-assets on build.

.PARAMETER LegacyWpfExe
  Deprecated (CMD-517). The legacy WPF executable no longer exists; this switch is ignored
  with a warning. Alias: -Wpf.

.PARAMETER ExtendedDiagnostics
  Pass --extended-diagnostics to the app. Writes a comprehensive agent-oriented log at
  %LocalAppData%\ChatGPTWrapper\wrapper-diagnostics.jsonl (all channels + legacy mirrors).

.PARAMETER LogUiEvents
  Pass --log-ui-events to the app. Logs shell events (tabs, navigation, arm state).
  Implied when -ExtendedDiagnostics is set.

.PARAMETER SkipClean
  Skip deleting .build-out and dotnet clean. Faster iteration when only code changed.

.PARAMETER RemainingArgs
  Additional arguments forwarded to the active host executable after diagnostic flags.

.EXAMPLE
  .\run.ps1

.EXAMPLE
  .\run.ps1 -ExtendedDiagnostics

.EXAMPLE
  .\run.ps1 -SkipClean
#>
param(
    [Alias('Wpf')]
    [switch]$LegacyWpfExe,

    [Alias('ExtendedDiagonstics')]
    [switch]$ExtendedDiagnostics,

    [switch]$LogUiEvents,

    [switch]$SkipClean,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArgs
)

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$solution = Join-Path $repoRoot 'chatgpt-wrapper.sln'
$winUiProject = Join-Path $repoRoot 'ChatGPTWrapper.WinUI\ChatGPTWrapper.WinUI.csproj'
$outDir = Join-Path $repoRoot '.build-out'
$exe = Join-Path $outDir 'ChatGPT Wrapper WinUI.exe'
$hostLabel = 'WinUI'
$processNames = @('ChatGPT Wrapper WinUI', 'ChatGPT Wrapper')
$logsDir = Join-Path $env:LOCALAPPDATA 'ChatGPTWrapper'

function Stop-WrapperProcesses {
    $stoppedAny = $false
    foreach ($processName in $processNames) {
        $running = Get-Process -Name $processName -ErrorAction SilentlyContinue
        if ($running) {
            Write-Host "Stopping $($running.Count) running $processName instance(s)..."
            $running | Stop-Process -Force -ErrorAction SilentlyContinue
            $stoppedAny = $true
        }
    }

    if ($stoppedAny) {
        Start-Sleep -Milliseconds 800
    }
}

function Clear-BuildOutputDirectory {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    Write-Host "Removing previous output: $Path"

    for ($attempt = 1; $attempt -le 4; $attempt++) {
        try {
            Get-ChildItem -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue |
                ForEach-Object { $_.Attributes = 'Normal' }
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            if ($attempt -eq 4) {
                Write-Host "Remove-Item failed ($($_.Exception.Message)); trying rmdir /s /q..."
                cmd /c "rmdir /s /q `"$Path`"" | Out-Null
                if (Test-Path -LiteralPath $Path) {
                    throw "Could not delete $Path. Close ChatGPT Wrapper and any tools using .build-out, then retry."
                }
                return
            }

            Write-Host "Retrying output cleanup (attempt $($attempt + 1)/4)..."
            Stop-WrapperProcesses
            Start-Sleep -Milliseconds 800
        }
    }
}

if ($LegacyWpfExe) {
    Write-Warning 'The legacy WPF executable was removed in Phase 6 (CMD-517). Launching WinUI instead.'
}

Stop-WrapperProcesses

if (-not $SkipClean) {
    Clear-BuildOutputDirectory -Path $outDir

    Write-Host 'Cleaning solution...'
    dotnet clean $solution -c Debug --verbosity minimal
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
else {
    Write-Host 'SkipClean: keeping existing .build-out and skipping dotnet clean.'
}

Write-Host "Building ChatGPT Wrapper ($hostLabel) -> $outDir"
dotnet build $winUiProject -c Debug -o $outDir --verbosity minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not (Test-Path -LiteralPath $exe)) {
    Write-Error "Build succeeded but executable was not found: $exe"
}

$launchArgs = [System.Collections.Generic.List[string]]::new()
if ($ExtendedDiagnostics) {
    $launchArgs.Add('--extended-diagnostics')
    if (-not $LogUiEvents) {
        $launchArgs.Add('--log-ui-events')
    }
}
if ($LogUiEvents) {
    $launchArgs.Add('--log-ui-events')
}
if ($RemainingArgs) {
    $launchArgs.AddRange($RemainingArgs)
}

Write-Host "Starting $hostLabel host: $exe"
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
