# Builds, tests and publishes the Windows client and signaling server.
# Usage: pwsh -File scripts/build-windows.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet build LinkView.slnx -c Release
    dotnet test LinkView.slnx -c Release
    dotnet publish src/LinkView.Windows/LinkView.Windows.csproj -c Release -r win-x64 --self-contained false -o dist/windows
    dotnet publish src/LinkView.Signaling/LinkView.Signaling.csproj -c Release -o dist/signaling
    Write-Host "Artifacts written to dist/windows and dist/signaling"
}
finally {
    Pop-Location
}
