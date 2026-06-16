param(
    [switch]$Live,
    [switch]$Open,
    [switch]$SkipUpload,
    [switch]$DownloadOnly,
    [switch]$CleanupProbe
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$project = Join-Path $repoRoot "tests\ChatGPTWrapper.ApiDiagnostics\ChatGPTWrapper.ApiDiagnostics.csproj"

Write-Host "Building ChatGPT Wrapper and performance tests..."
dotnet build $project -c Debug | Out-Host

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed. Fix compile errors before running performance tests."
    exit $LASTEXITCODE
}

if ($Live) {
    $env:CGW_RUN_LIVE_API_TESTS = "1"
    $filter = "Category=Performance&Category=Live"
    Write-Host "Running live source sync performance tests (CGW_RUN_LIVE_API_TESTS=1)..."

    if (-not $PSBoundParameters.ContainsKey("CleanupProbe") -or $CleanupProbe) {
        $env:CGW_PERF_CLEANUP_PROBE = "1"
    } else {
        $env:CGW_PERF_CLEANUP_PROBE = "0"
    }
} else {
    $filter = "Category=Performance&Category!=Live"
    Write-Host "Running unit/integration source sync performance tests..."
}

if ($SkipUpload -or $DownloadOnly) {
    $env:CGW_PERF_SKIP_UPLOAD = "1"
    Write-Host "Skipping upload/attach/apply steps (CGW_PERF_SKIP_UPLOAD=1)..."
}

dotnet test $project --no-build -c Debug --filter $filter | Out-Host
$testExit = $LASTEXITCODE

$reportJson = Join-Path $env:LOCALAPPDATA "ChatGPTWrapper\source-sync-perf-report.json"
$reportText = Join-Path $env:LOCALAPPDATA "ChatGPTWrapper\source-sync-perf-report.txt"

Write-Host ""

if ($testExit -ne 0) {
    Write-Error "Performance tests failed (exit code $testExit)."

    if (Test-Path $reportText) {
        Write-Host ""
        Write-Host "Partial report: $reportText"
        Get-Content $reportText
    }

    exit $testExit
}

Write-Host "Reports:"
Write-Host "  $reportJson"
Write-Host "  $reportText"

if (Test-Path $reportText) {
    Write-Host ""
    Get-Content $reportText

    if ($Open) {
        Start-Process $reportText
    }
} else {
    Write-Warning "Report not found at $reportText"
}
