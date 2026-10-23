# Starts the signaling server, runs the two-session end-to-end test, then stops the server.
# Requires the solution to be built first (dotnet build).
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dll = Join-Path $root "src/LinkView.Signaling/bin/Debug/net10.0/LinkView.Signaling.dll"
if (-not (Test-Path $dll)) {
    dotnet build (Join-Path $root "src/LinkView.Signaling/LinkView.Signaling.csproj")
}

$server = Start-Process -FilePath "dotnet" -ArgumentList "`"$dll`"" -PassThru -WindowStyle Hidden
try {
    $ready = $false
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Milliseconds 300
        try {
            $r = Invoke-WebRequest -Uri "http://127.0.0.1:5180/healthz" -TimeoutSec 2 -UseBasicParsing
            if ($r.StatusCode -eq 200) { $ready = $true; break }
        }
        catch { }
    }
    if (-not $ready) { throw "Signaling server did not become ready." }
    dotnet run --project (Join-Path $root "tools/LinkView.E2E/LinkView.E2E.csproj") --no-build
    $code = $LASTEXITCODE
    exit $code
}
finally {
    if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id -Force }
}
