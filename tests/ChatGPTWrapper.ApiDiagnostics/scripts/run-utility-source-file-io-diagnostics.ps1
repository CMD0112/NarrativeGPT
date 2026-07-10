# Live utility source file I/O diagnostics (CMD-442).
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$testProject = Join-Path $repoRoot "tests\ChatGPTWrapper.ApiDiagnostics\ChatGPTWrapper.ApiDiagnostics.csproj"

Write-Host "Building diagnostic tests..."
dotnet build $testProject -c Debug | Out-Host

$env:CGW_RUN_LIVE_API_TESTS = "1"

if (-not $env:CGW_UTILITY_SOURCE_IO_GIZMO_ID -and $env:CGW_DOWNLOAD_GIZMO_ID) {
    $env:CGW_UTILITY_SOURCE_IO_GIZMO_ID = $env:CGW_DOWNLOAD_GIZMO_ID
}

if ($env:CGW_UTILITY_SOURCE_IO_GIZMO_ID) {
    Write-Host "Gizmo: $env:CGW_UTILITY_SOURCE_IO_GIZMO_ID"
} else {
    Write-Host "Gizmo: (auto — first Snorlax project)"
}

if ($env:CGW_UTILITY_SOURCE_IO_UPLOAD_METHOD) {
    Write-Host "Upload method: $env:CGW_UTILITY_SOURCE_IO_UPLOAD_METHOD"
} else {
    Write-Host "Upload method: PureApi (default; set CGW_UTILITY_SOURCE_IO_UPLOAD_METHOD to override)"
}

$runE2e = $args -contains "-E2E" -or $env:CGW_UTILITY_SOURCE_IO_E2E -eq "1" -or $env:CGW_UTILITY_SOURCE_IO_E2E -eq "true"
if ($runE2e) {
    $env:CGW_UTILITY_SOURCE_IO_E2E = "1"
    Write-Host "E2E: enabled (utility thread + pointer send + response extract)"
    if ($env:CGW_UTILITY_SOURCE_IO_CONVERSATION_ID) {
        Write-Host "  Conversation: $env:CGW_UTILITY_SOURCE_IO_CONVERSATION_ID (reuse pinned thread)"
    } else {
        Write-Host "  Conversation: ephemeral create (set CGW_UTILITY_SOURCE_IO_CONVERSATION_ID to reuse)"
    }
}

$filter = if ($runE2e) {
    "FullyQualifiedName~LiveUtilitySourceFileIoTests"
} else {
    "FullyQualifiedName~LiveUtilitySourceFileIoTests.Run_utility_source_file_io_checklist"
}

Write-Host "Running utility source file I/O diagnostics..."
Write-Host "  Live progress: $env:LOCALAPPDATA\ChatGPTWrapper\utility-source-file-io-report.txt"

dotnet test $testProject --no-build -c Debug `
    --filter $filter | Out-Host

$reportTxt = Join-Path $env:LOCALAPPDATA "ChatGPTWrapper\utility-source-file-io-report.txt"
$reportJson = Join-Path $env:LOCALAPPDATA "ChatGPTWrapper\utility-source-file-io-report.json"

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
