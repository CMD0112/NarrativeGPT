# Runs ApiDiagnostics tests with file-lock hygiene (stops stale testhost / app processes first).
param(
    [string]$Filter = "Category=Unit",
    [switch]$NoBuild,
    [switch]$Live
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$testProject = Join-Path $repoRoot "tests\ChatGPTWrapper.ApiDiagnostics\ChatGPTWrapper.ApiDiagnostics.csproj"

$stale = @(
    Get-Process -Name "testhost" -ErrorAction SilentlyContinue
    Get-Process -Name "ChatGPT Wrapper" -ErrorAction SilentlyContinue
    Get-Process -Name "vstest.console" -ErrorAction SilentlyContinue
) | Where-Object { $_ }

if ($stale) {
    Write-Host "Stopping $($stale.Count) stale test/app process(es) to avoid build file locks..."
    $stale | Stop-Process -Force
    Start-Sleep -Milliseconds 400
}

if (-not $NoBuild) {
    Write-Host "Building diagnostic tests..."
    dotnet build $testProject -c Debug | Out-Host
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

if ($Live) {
    $env:CGW_RUN_LIVE_API_TESTS = "1"
    if ([string]::IsNullOrWhiteSpace($Filter)) {
        $Filter = "Category=Live"
    }
}

$env:CGW_TEST_EXTENDED_DIAGNOSTICS = "1"

$testArgs = @(
    "test", $testProject,
    "-c", "Debug",
    "--filter", $Filter
)
if ($NoBuild) {
    $testArgs += "--no-build"
}

Write-Host "Running: dotnet $($testArgs -join ' ')"
dotnet @testArgs | Out-Host
exit $LASTEXITCODE
