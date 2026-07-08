param(
    [string]$Tag = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$cargoTomlPath = Join-Path $repoRoot "rust-server/Cargo.toml"
$packageJsonPath = Join-Path $repoRoot "unity-package/package.json"
$serverJsonPath = Join-Path $repoRoot "server.json"

if (-not (Test-Path $cargoTomlPath)) {
    throw "Missing file: $cargoTomlPath"
}

if (-not (Test-Path $packageJsonPath)) {
    throw "Missing file: $packageJsonPath"
}

if (-not (Test-Path $serverJsonPath)) {
    throw "Missing file: $serverJsonPath"
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

$serverJson = Get-Content $serverJsonPath -Raw | ConvertFrom-Json
$serverVersion = [string]$serverJson.version
$serverPackageVersion = [string]$serverJson.packages[0].version
$serverPackageIdentifier = [string]$serverJson.packages[0].identifier
$mcpName = [string]$packageJson.mcpName
$serverName = [string]$serverJson.name

if ([string]::IsNullOrWhiteSpace($mcpName)) {
    throw "Could not read MCP Registry mcpName from unity-package/package.json."
}

if ($mcpName -ne $serverName) {
    throw "MCP Registry name mismatch. package.json mcpName=$mcpName server.json name=$serverName"
}

if ($packageJson.name -ne $serverPackageIdentifier) {
    throw "MCP Registry package identifier mismatch. package.json name=$($packageJson.name) server.json identifier=$serverPackageIdentifier"
}

if ($packageVersion -ne $serverVersion) {
    throw "MCP Registry version mismatch. unity-package=$packageVersion server.json=$serverVersion"
}

if ($packageVersion -ne $serverPackageVersion) {
    throw "MCP Registry package version mismatch. unity-package=$packageVersion server.json package=$serverPackageVersion"
}

if (-not [string]::IsNullOrWhiteSpace($Tag)) {
    $expectedTag = "v$packageVersion"
    if ($Tag -ne $expectedTag) {
        throw "Git tag mismatch. expected=$expectedTag actual=$Tag"
    }
}

Write-Host "Version sync OK: $packageVersion"
