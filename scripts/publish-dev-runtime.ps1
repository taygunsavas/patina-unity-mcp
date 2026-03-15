param(
    [string]$BinaryPath,
    [string]$OutputRoot = "dist/dev-runtime/current"
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

$binaryExtension = Get-BinaryExtension
$platformDirectory = Get-PlatformDirectory

if ([string]::IsNullOrWhiteSpace($BinaryPath)) {
    $BinaryPath = Join-Path "rust-server/target/release" ("patina-server" + $binaryExtension)
}

if (-not (Test-Path $BinaryPath)) {
    throw "Server binary not found: $BinaryPath"
}

$resolvedBinary = (Resolve-Path $BinaryPath).Path
$outputDirectory = Join-Path $OutputRoot $platformDirectory
$resolvedOutputDirectory = Join-Path (Get-Location) $outputDirectory

New-Item -ItemType Directory -Force -Path $resolvedOutputDirectory | Out-Null

$destinationPath = Join-Path $resolvedOutputDirectory ("patina-server" + $binaryExtension)
Copy-Item -Path $resolvedBinary -Destination $destinationPath -Force

Write-Host "Development runtime published:"
Write-Host "  Source      : $resolvedBinary"
Write-Host "  Destination : $destinationPath"

