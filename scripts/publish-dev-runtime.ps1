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

# Relative paths are resolved against the repository root, not the caller's working
# directory, so the script publishes to the location Unity probes no matter where it is
# invoked from. Keep this in sync with publish-dev-runtime.sh.
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

function Resolve-AgainstRepositoryRoot([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $repositoryRoot $Path
}

if ([string]::IsNullOrWhiteSpace($BinaryPath)) {
    $BinaryPath = Join-Path "rust-server/target/release" ("patina-server" + $binaryExtension)
}

$BinaryPath = Resolve-AgainstRepositoryRoot $BinaryPath

if (-not (Test-Path $BinaryPath)) {
    throw "Server binary not found: $BinaryPath"
}

$resolvedBinary = (Resolve-Path $BinaryPath).Path
$resolvedOutputDirectory = Join-Path (Resolve-AgainstRepositoryRoot $OutputRoot) $platformDirectory

New-Item -ItemType Directory -Force -Path $resolvedOutputDirectory | Out-Null

$destinationPath = Join-Path $resolvedOutputDirectory ("patina-server" + $binaryExtension)

# Publish through a temporary file, move any existing binary aside, then move the new
# one into place. Writing over the destination keeps its inode; on macOS that
# invalidates the kernel's code-signing pages for a binary an MCP host is still
# running, and every later exec() of that path is SIGKILLed (exit 137). See issue #99.
# Move-Item -Force is not a rename when the destination exists -- PowerShell deletes the
# destination first -- so the old file is renamed out of the way explicitly instead. That
# also keeps the destination from ever being briefly absent, and lets the publish succeed
# on Windows while a host still holds the running .exe open.
$temporaryPath = $destinationPath + ".tmp-" + [System.Guid]::NewGuid().ToString("N")
$backupPath = $destinationPath + ".old-" + [System.Guid]::NewGuid().ToString("N")
$movedAside = $false

try {
    Copy-Item -Path $resolvedBinary -Destination $temporaryPath -Force

    if (Test-Path $destinationPath) {
        Move-Item -Path $destinationPath -Destination $backupPath
        $movedAside = $true
    }

    Move-Item -Path $temporaryPath -Destination $destinationPath

    if ($movedAside) {
        Remove-Item -Path $backupPath -Force -ErrorAction SilentlyContinue
        if (Test-Path $backupPath) {
            Write-Warning "Could not remove the previous runtime at $backupPath. Delete it manually once no host is using it."
        }
    }
}
catch {
    if ($movedAside -and -not (Test-Path $destinationPath)) {
        Move-Item -Path $backupPath -Destination $destinationPath -ErrorAction SilentlyContinue

        if (-not (Test-Path $destinationPath)) {
            Write-Warning "Publish failed and the previous runtime could not be restored. A working copy is still at $backupPath -- rename it back to $destinationPath manually."
        }
    }

    throw
}
finally {
    if (Test-Path $temporaryPath) {
        Remove-Item -Path $temporaryPath -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "Development runtime published:"
Write-Host "  Source      : $resolvedBinary"
Write-Host "  Destination : $destinationPath"

