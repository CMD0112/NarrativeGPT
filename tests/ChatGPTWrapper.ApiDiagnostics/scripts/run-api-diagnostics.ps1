# Runs live WebView2 API diagnostics (requires signed-in shared profile).
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$testProject = Join-Path $repoRoot "tests\ChatGPTWrapper.ApiDiagnostics\ChatGPTWrapper.ApiDiagnostics.csproj"

Write-Host "Building ChatGPT Wrapper and diagnostic tests..."
dotnet build $testProject -c Debug | Out-Host

$env:CGW_RUN_LIVE_API_TESTS = "1"
Write-Host "Running live API diagnostics (CGW_RUN_LIVE_API_TESTS=1)..."
dotnet test $testProject --no-build -c Debug --filter "Category=Live" | Out-Host

$reportTxt = Join-Path $env:LOCALAPPDATA "ChatGPTWrapper\api-diagnostic-report.txt"
$reportJson = Join-Path $env:LOCALAPPDATA "ChatGPTWrapper\api-diagnostic-report.json"

if (Test-Path $reportTxt) {
    Write-Host ""
    Write-Host "Report: $reportTxt"
    Get-Content $reportTxt
    if ($args -contains "-Open") {
        Start-Process $reportTxt
    }
} else {
    Write-Warning "Report not found at $reportTxt"
}

if (Test-Path $reportJson) {
    Write-Host "JSON: $reportJson"
}
