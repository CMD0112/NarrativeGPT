#Requires -Version 5.0
<#
.SYNOPSIS
  Builds a self-contained Windows x64 folder and a .zip you can share (no separate .NET install).

.NOTES
  WebView2 Runtime is still required (normally installed with Edge); see README.txt in the output.
#>
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$proj = Join-Path $here 'ChatGPTWrapper.csproj'
$distFolderName = 'ChatGPT-Wrapper-windows-x64'
$outDir = Join-Path $here "dist\$distFolderName"

Write-Host "Publishing self-contained win-x64 -> $outDir"

dotnet publish $proj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishTrimmed=false `
    -o $outDir

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

# Self-contained builds include this debugger helper; end users do not need it.
Remove-Item (Join-Path $outDir 'createdump.exe') -ErrorAction SilentlyContinue

$readme = @'
ChatGPT Wrapper — portable build (Windows x64)


Run "ChatGPT Wrapper.exe".


Requirements

  • Windows 10 or 11 (64-bit)

  • Microsoft Edge WebView2 Runtime — usually already installed with Microsoft Edge.
    If the embedded browser stays blank, install the Evergreen runtime from:
    https://developer.microsoft.com/microsoft-edge/webview2/


This package includes the .NET runtime. You do not need to install .NET separately.
'@
Set-Content -Path (Join-Path $outDir 'README.txt') -Encoding utf8 -Value $readme.Trim()

$zipPath = Join-Path $here "dist\$distFolderName.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path $outDir -DestinationPath $zipPath -CompressionLevel Optimal -Force

$len = (Get-Item $zipPath).Length
Write-Host ""
Write-Host "Done."
Write-Host "  Folder: $outDir"
Write-Host "  Zip:    $zipPath"
Write-Host ('  Zip size: {0:N1} MB' -f ($len / 1MB))
