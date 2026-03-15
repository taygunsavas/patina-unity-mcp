param(
    [string]$OutputPath = "dist/local-upm/com.taygunsavas.patina-unity-mcp",
    [string]$PackagePath = "unity-package",
    [string]$BinaryPath
)

$ErrorActionPreference = "Stop"

function Get-PlatformDirectory {
    if ($IsWindows) {
        return "x86_64-win"
    }

    if ($IsMacOS) {
        $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
        if ($architecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
            return "aarch64-macos"
        }

        return "x86_64-macos"
    }

    return "x86_64-linux"
}

function Get-BinaryExtension {
    if ($IsWindows) {
        return ".exe"
    }

    return ""
}

function Resolve-ExistingPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PathValue,
        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if (-not (Test-Path $PathValue)) {
        throw "$Description not found: $PathValue"
    }

    return (Resolve-Path $PathValue).Path
}

$packageRoot = Resolve-ExistingPath -PathValue $PackagePath -Description "Unity package path"
$binaryExtension = Get-BinaryExtension
$platformDirectory = Get-PlatformDirectory

if ([string]::IsNullOrWhiteSpace($BinaryPath)) {
    $BinaryPath = Join-Path "rust-server/target/release" ("patina-server" + $binaryExtension)
}

$serverBinary = Resolve-ExistingPath -PathValue $BinaryPath -Description "Server binary"
$stagingRoot = Join-Path (Get-Location) $OutputPath

if (Test-Path $stagingRoot) {
    Remove-Item -Recurse -Force $stagingRoot
}

New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null
Copy-Item -Path (Join-Path $packageRoot "*") -Destination $stagingRoot -Recurse -Force

$pluginTargetDirectory = Join-Path $stagingRoot ("Plugins/" + $platformDirectory)
New-Item -ItemType Directory -Force -Path $pluginTargetDirectory | Out-Null

$binaryName = "patina-server" + $binaryExtension
$stagedBinaryPath = Join-Path $pluginTargetDirectory $binaryName
Copy-Item -Path $serverBinary -Destination $stagedBinaryPath -Force

$manifestPath = Join-Path $stagingRoot "package.json"
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

Write-Host "Local UPM package staged:"
Write-Host "  Package root : $stagingRoot"
Write-Host "  Package json : $manifestPath"
Write-Host "  Version      : $($manifest.version)"
Write-Host "  Binary       : $stagedBinaryPath"



