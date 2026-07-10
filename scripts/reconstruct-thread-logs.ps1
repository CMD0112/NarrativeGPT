param(
    [Parameter(Mandatory = $true)]
    [string]$AdventureDir
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$testProject = Join-Path $repoRoot "tests\ChatGPTWrapper.ApiDiagnostics\ChatGPTWrapper.ApiDiagnostics.csproj"

if (-not (Test-Path $AdventureDir)) {
    throw "Adventure directory not found: $AdventureDir"
}

Write-Host "Building diagnostic tests..."
dotnet build $testProject -c Debug | Out-Host

$env:CGW_RECONSTRUCT_ADVENTURE_DIR = (Resolve-Path $AdventureDir).Path
Write-Host "Reconstructing thread logs in: $env:CGW_RECONSTRUCT_ADVENTURE_DIR"

dotnet test $testProject --no-build -c Debug `
    --filter "FullyQualifiedName~ThreadLogReconstructionDiagnosticTests" | Out-Host

$reportTxt = Join-Path $env:LOCALAPPDATA "ChatGPTWrapper\thread-log-reconstruction-report.txt"
if (Test-Path $reportTxt) {
    Write-Host ""
    Write-Host "Report: $reportTxt"
    Get-Content $reportTxt
} else {
    Write-Warning "Report not found at $reportTxt"
}
