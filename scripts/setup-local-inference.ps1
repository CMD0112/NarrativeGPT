# Sets up Ollama + default model for Local Inference Lab (isolated from main app workflows).
param(
    [string]$Model = "qwen2.5:7b-instruct",
    [switch]$SkipModelPull,
    [switch]$RunLiveTests
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$labProject = Join-Path $repoRoot "ChatGPTWrapper.LocalInferenceLab\ChatGPTWrapper.LocalInferenceLab.csproj"
$testProject = Join-Path $repoRoot "tests\ChatGPTWrapper.ApiDiagnostics\ChatGPTWrapper.ApiDiagnostics.csproj"

function Get-OllamaExe {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Ollama\ollama.exe"),
        (Get-Command ollama -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
    ) | Where-Object { $_ -and (Test-Path $_) }

    if ($candidates.Count -eq 0) {
        throw "Ollama not found. Install from https://ollama.com or run: winget install Ollama.Ollama"
    }

    return $candidates[0]
}

function Test-OllamaReachable {
    param([string]$OllamaExe)
    try {
        & $OllamaExe list 2>&1 | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

Write-Host "=== Local Inference Lab setup ===" -ForegroundColor Cyan
Write-Host "Repo: $repoRoot"
Write-Host "Default model: $Model"
Write-Host ""

$ollamaExe = Get-OllamaExe
Write-Host "Ollama: $ollamaExe"
& $ollamaExe --version

if (-not (Test-OllamaReachable -OllamaExe $ollamaExe)) {
    Write-Host ""
    Write-Host "Ollama server is not reachable yet." -ForegroundColor Yellow
    Write-Host "On Windows, launch Ollama from the Start menu (tray app), then re-run this script."
    exit 2
}

if (-not $SkipModelPull) {
    $listOutput = (& $ollamaExe list 2>&1 | Out-String)
    if ($listOutput -notmatch [regex]::Escape($Model)) {
        Write-Host ""
        Write-Host "Pulling model '$Model' (first run may take several minutes)..." -ForegroundColor Cyan
        & $ollamaExe pull $Model
    }
    else {
        Write-Host "Model '$Model' already installed; skipping pull."
    }
}

$env:CGW_OLLAMA_MODEL = $Model
$env:CGW_OLLAMA_BASE_URL = "http://127.0.0.1:11434"

Write-Host ""
Write-Host "Building lab + tests..." -ForegroundColor Cyan
dotnet build $labProject -c Debug | Out-Host
dotnet build $testProject -c Debug | Out-Host

Write-Host ""
Write-Host "Probing server..." -ForegroundColor Cyan
dotnet run --project $labProject -c Debug --no-build -- probe
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Quick chat smoke test..." -ForegroundColor Cyan
dotnet run --project $labProject -c Debug --no-build -- chat "Reply with exactly: pong"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($RunLiveTests) {
    Write-Host ""
    Write-Host "Running gated Ollama live tests..." -ForegroundColor Cyan
    $env:CGW_RUN_OLLAMA_TESTS = "1"
    dotnet test $testProject --no-build -c Debug --filter "FullyQualifiedName~OllamaLiveTests" | Out-Host
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host ""
Write-Host "Setup complete." -ForegroundColor Green
Write-Host "Try: dotnet run --project ChatGPTWrapper.LocalInferenceLab -- entity-demo"
Write-Host "Env: CGW_OLLAMA_MODEL=$($env:CGW_OLLAMA_MODEL)"
