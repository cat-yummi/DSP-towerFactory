# 仅打包 Thunderstore zip（假定 Release 已编译成功）
$ErrorActionPreference = "Stop"
$ModName = "TowerFactory"
$OutputZip = Join-Path $PSScriptRoot "bin\Release\TowerFactory.zip"
$Dll = Join-Path $PSScriptRoot "bin\Release\net472\$ModName.dll"

if (-not (Test-Path $Dll)) {
    Write-Error "找不到 $Dll，请先 dotnet build -c Release"
    exit 1
}

$TempDir = Join-Path $PSScriptRoot "thunderstore_package"
if (Test-Path $TempDir) {
    Remove-Item $TempDir -Recurse -Force
}
New-Item -ItemType Directory -Path $TempDir | Out-Null

Copy-Item (Join-Path $PSScriptRoot "manifest.json") -Destination $TempDir
Copy-Item (Join-Path $PSScriptRoot "README.md") -Destination $TempDir
Copy-Item (Join-Path $PSScriptRoot "icon.png") -Destination $TempDir
Copy-Item $Dll -Destination $TempDir

if (Test-Path $OutputZip) {
    Remove-Item $OutputZip -Force
}
Compress-Archive -Path (Join-Path $TempDir "*") -DestinationPath $OutputZip -Force
Remove-Item $TempDir -Recurse -Force

Write-Host "Package created: $OutputZip" -ForegroundColor Green
