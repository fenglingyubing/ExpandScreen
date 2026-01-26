param(
    [string]$ProjectDir = "android-client",
    [string]$OutDir = "artifacts/android/apk",
    [switch]$IncludeX86_64,
    [switch]$RequireSigning = $true
)

$ErrorActionPreference = "Stop"

function Test-AndroidSigningConfigured {
    param([string]$ProjectDir)

    $keystorePropsPath = Join-Path $ProjectDir "keystore.properties"
    if (Test-Path $keystorePropsPath) { return $true }

    $requiredEnvVars = @(
        "ANDROID_KEYSTORE_FILE",
        "ANDROID_KEYSTORE_PASSWORD",
        "ANDROID_KEY_ALIAS",
        "ANDROID_KEY_PASSWORD"
    )
    foreach ($name in $requiredEnvVars) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if ([string]::IsNullOrWhiteSpace($value)) { return $false }
    }
    return $true
}

if (-not (Test-Path $ProjectDir)) {
    throw "ProjectDir not found: $ProjectDir"
}

$projectDirResolved = (Resolve-Path $ProjectDir).Path
$gradlew = Join-Path $projectDirResolved "gradlew.bat"
if (-not (Test-Path $gradlew)) {
    throw "gradlew.bat not found: $gradlew"
}

if ($RequireSigning -and -not (Test-AndroidSigningConfigured -ProjectDir $ProjectDir)) {
    throw "Release signing is not configured. Create '$ProjectDir/keystore.properties' (see '$ProjectDir/keystore.properties.example') or set ANDROID_KEYSTORE_* env vars."
}

$gradleArgs = @(":app:clean", ":app:assembleRelease", "--no-daemon")
if ($IncludeX86_64) {
    $gradleArgs = @("-PincludeX86_64=true") + $gradleArgs
}

Push-Location $projectDirResolved
try {
    & ".\\gradlew.bat" @gradleArgs
    if ($LASTEXITCODE -ne 0) { throw "Gradle failed with exit code $LASTEXITCODE" }
} finally {
    Pop-Location
}

$releaseDir = Join-Path $projectDirResolved "app/build/outputs/apk/release"
$metadataPath = Join-Path $releaseDir "output-metadata.json"
if (-not (Test-Path $metadataPath)) {
    throw "output-metadata.json not found: $metadataPath"
}

$metadata = Get-Content $metadataPath -Raw | ConvertFrom-Json
$element = $metadata.elements | Select-Object -First 1
$versionName = $element.versionName
$versionCode = $element.versionCode

$stagingDir = Join-Path $OutDir ("{0}-{1}" -f $versionName, $versionCode)
New-Item -ItemType Directory -Force -Path $stagingDir | Out-Null

Copy-Item -Force $metadataPath (Join-Path $stagingDir "output-metadata.json")

$apks = Get-ChildItem -Path $releaseDir -Filter "*.apk" -File
if ($apks.Count -eq 0) {
    throw "No APKs found in: $releaseDir"
}

foreach ($apk in $apks) {
    Copy-Item -Force $apk.FullName (Join-Path $stagingDir $apk.Name)
}

Write-Host "Release APKs staged to: $stagingDir"
Write-Host ("- Version: {0} ({1})" -f $versionName, $versionCode)
Write-Host ("- Outputs: {0}" -f (($apks | ForEach-Object { $_.Name }) -join ", "))
