# Live project source download diagnostics (project-scoped API paths).
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$testProject = Join-Path $repoRoot "tests\ChatGPTWrapper.ApiDiagnostics\ChatGPTWrapper.ApiDiagnostics.csproj"

Write-Host "Building diagnostic tests..."
dotnet build $testProject -c Debug | Out-Host

$env:CGW_RUN_LIVE_API_TESTS = "1"

if ($env:CGW_DOWNLOAD_GIZMO_ID) {
    Write-Host "Gizmo: $env:CGW_DOWNLOAD_GIZMO_ID"
}
if ($env:CGW_DOWNLOAD_FILE_ID) {
    Write-Host "File id filter: $env:CGW_DOWNLOAD_FILE_ID"
}

Write-Host "Running project source download diagnostics..."
dotnet test $testProject --no-build -c Debug `
    --filter "FullyQualifiedName~LiveProjectSourceDownloadTests" | Out-Host

$reportTxt = Join-Path $env:LOCALAPPDATA "ChatGPTWrapper\project-source-download-report.txt"
$reportJson = Join-Path $env:LOCALAPPDATA "ChatGPTWrapper\project-source-download-report.json"

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
