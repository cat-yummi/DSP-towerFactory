# Thunderstore Package Builder

$ModName = "TowerFactory"
$OutputZip = ".\bin\Release\TowerFactory.zip"

# Build the project
Write-Host "Building $ModName..." -ForegroundColor Cyan
dotnet build -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "Build successful!" -ForegroundColor Green

# Create temp directory
$TempDir = ".\thunderstore_package"
if (Test-Path $TempDir) {
    Remove-Item $TempDir -Recurse -Force
}
New-Item -ItemType Directory -Path $TempDir | Out-Null

# Copy files
Copy-Item ".\manifest.json" -Destination $TempDir
Copy-Item ".\README.md" -Destination $TempDir
Copy-Item ".\icon.png" -Destination $TempDir
Copy-Item ".\bin\Release\net472\$ModName.dll" -Destination $TempDir

# Create zip
if (Test-Path $OutputZip) {
    Remove-Item $OutputZip -Force
}

Compress-Archive -Path "$TempDir\*" -DestinationPath $OutputZip -Force

# Cleanup
Remove-Item $TempDir -Recurse -Force

Write-Host "Package created: $OutputZip" -ForegroundColor Green
