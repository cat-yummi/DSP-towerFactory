# Thunderstore：编译 + 打包（打包也会在 Release 构建后自动执行）
$ErrorActionPreference = "Stop"
$ModName = "TowerFactory"

Write-Host "Building $ModName..." -ForegroundColor Cyan
dotnet build -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "Build successful!" -ForegroundColor Green

& (Join-Path $PSScriptRoot "package_thunderstore.ps1")
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
