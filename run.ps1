#Requires -Version 5.0
$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$project = Join-Path $repoRoot 'ChatGPTWrapper\ChatGPTWrapper.csproj'

Write-Host 'Building ChatGPT Wrapper...'
dotnet build $project -c Debug
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host 'Starting ChatGPT Wrapper...'
dotnet run --project $project -c Debug --no-build
