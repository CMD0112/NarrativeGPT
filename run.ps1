#Requires -Version 5.0
<#
.SYNOPSIS
  Clean-build and run ChatGPT Wrapper from the repo-local .build-out folder.

  Always stops any running instance, removes stale output, rebuilds, and launches
  .build-out\ChatGPT Wrapper.exe so you never accidentally run an old build.
#>
$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$project = Join-Path $repoRoot 'ChatGPTWrapper\ChatGPTWrapper.csproj'
$outDir = Join-Path $repoRoot '.build-out'
$exe = Join-Path $outDir 'ChatGPT Wrapper.exe'
$processName = 'ChatGPT Wrapper'

$running = Get-Process -Name $processName -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Stopping $($running.Count) running $processName instance(s)..."
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

Write-Host "Removing previous output: $outDir"
if (Test-Path $outDir) {
    Remove-Item $outDir -Recurse -Force
}

Write-Host 'Cleaning project (bin/obj)...'
dotnet clean $project -c Debug --verbosity minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Building ChatGPT Wrapper -> $outDir"
dotnet build $project -c Debug -o $outDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not (Test-Path -LiteralPath $exe)) {
    Write-Error "Build succeeded but executable was not found: $exe"
}

Write-Host "Starting $exe"
& $exe @args
