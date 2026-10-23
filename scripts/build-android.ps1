# Builds the Android debug APK.
# Android Gradle Plugin rejects non-ASCII project paths on Windows, so this
# script copies the project to an ASCII staging directory before building.
#
# Usage:
#   pwsh -File scripts/build-android.ps1 -JdkHome "C:\Program Files\Java\jdk-17" -SdkRoot "C:\Android\Sdk"
param(
    [Parameter(Mandatory = $true)] [string] $JdkHome,
    [Parameter(Mandatory = $true)] [string] $SdkRoot,
    [string] $StagingRoot = "D:\linkview-build"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$staging = Join-Path $StagingRoot "android"

if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
New-Item -ItemType Directory -Force -Path $StagingRoot | Out-Null
Copy-Item -Recurse (Join-Path $root "android") $staging

$sdkForProperties = $SdkRoot.Replace("\", "/")
Set-Content -Path (Join-Path $staging "local.properties") -Value "sdk.dir=$sdkForProperties" -Encoding ascii

$env:JAVA_HOME = $JdkHome
$env:ANDROID_HOME = $SdkRoot
Push-Location $staging
try {
    & .\gradlew.bat assembleDebug
    $apk = Join-Path $staging "app\build\outputs\apk\debug\app-debug.apk"
    if (Test-Path $apk) {
        New-Item -ItemType Directory -Force -Path (Join-Path $root "dist/android") | Out-Null
        Copy-Item $apk (Join-Path $root "dist/android/LinkView-0.9.0-debug.apk") -Force
        Write-Host "APK: dist/android/LinkView-0.9.0-debug.apk"
    }
}
finally {
    Pop-Location
}
