# Starts the LinkView signaling server from source.
# Usage: pwsh -File scripts/run-signaling.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
dotnet run --project (Join-Path $root "src/LinkView.Signaling/LinkView.Signaling.csproj")
