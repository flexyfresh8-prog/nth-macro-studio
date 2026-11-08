# NTH Macro Studio — one-command build script.
# Usage:
#   .\build.ps1                     framework-dependent build (fast, needs .NET 8 Desktop Runtime)
#   .\build.ps1 -SelfContained      standalone build (no runtime install needed, ~150 MB)
#   .\build.ps1 -OutDir dist2       custom output directory
param(
    [switch]$SelfContained,
    [string]$Configuration = "Release",
    [string]$OutDir = "dist"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $repoRoot "src\NthMacroStudio\NthMacroStudio.csproj"
$output = Join-Path $repoRoot $OutDir

Write-Host ""
Write-Host "== NTH Macro Studio build ==" -ForegroundColor Cyan
Write-Host " Configuration : $Configuration"
Write-Host " Self-contained: $SelfContained"
Write-Host " Output        : $output"
Write-Host ""

$scArg = if ($SelfContained) { "true" } else { "false" }

dotnet publish $project -c $Configuration -r win-x64 --self-contained $scArg -o $output
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build FAILED (exit code $LASTEXITCODE)." -ForegroundColor Red
    exit $LASTEXITCODE
}

$exe = Join-Path $output "NTH Macro Studio.exe"
Write-Host ""
Write-Host "Build succeeded." -ForegroundColor Green
Write-Host "Executable: $exe"
if (-not $SelfContained) {
    Write-Host ""
    Write-Host "Framework-dependent build notes:" -ForegroundColor Yellow
    Write-Host " - Requires the .NET 8 Desktop Runtime: https://dotnet.microsoft.com/download/dotnet/8.0"
    Write-Host " - Requires the WebView2 Runtime (preinstalled on Windows 11):"
    Write-Host "   https://developer.microsoft.com/microsoft-edge/webview2/"
}
else {
    Write-Host ""
    Write-Host "Self-contained build: no runtime installation is required on target machines."
}
Write-Host ""
