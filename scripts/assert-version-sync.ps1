param(
    [string]$Tag = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$cargoTomlPath = Join-Path $repoRoot "rust-server/Cargo.toml"
$packageJsonPath = Join-Path $repoRoot "unity-package/package.json"

if (-not (Test-Path $cargoTomlPath)) {
    throw "Missing file: $cargoTomlPath"
}

if (-not (Test-Path $packageJsonPath)) {
    throw "Missing file: $packageJsonPath"
}

$cargoToml = Get-Content $cargoTomlPath -Raw
$cargoVersionMatch = [regex]::Match($cargoToml, '(?m)^version\s*=\s*"(?<version>[^"]+)"\s*$')
if (-not $cargoVersionMatch.Success) {
    throw "Could not read Rust package version from Cargo.toml."
}

$rustVersion = $cargoVersionMatch.Groups["version"].Value
$packageJson = Get-Content $packageJsonPath -Raw | ConvertFrom-Json
$packageVersion = [string]$packageJson.version

if ([string]::IsNullOrWhiteSpace($packageVersion)) {
    throw "Could not read UPM package version from package.json."
}

if ($rustVersion -ne $packageVersion) {
    throw "Version mismatch. rust-server=$rustVersion unity-package=$packageVersion"
}

if (-not [string]::IsNullOrWhiteSpace($Tag)) {
    $expectedTag = "v$packageVersion"
    if ($Tag -ne $expectedTag) {
        throw "Git tag mismatch. expected=$expectedTag actual=$Tag"
    }
}

Write-Host "Version sync OK: $packageVersion"
