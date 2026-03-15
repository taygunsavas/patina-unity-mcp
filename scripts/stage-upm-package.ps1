param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePackagePath,

    [Parameter(Mandatory = $true)]
    [string]$ArtifactsPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

function Copy-DirectoryContent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,
        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Copy-Item -Path (Join-Path $Source "*") -Destination $Destination -Recurse -Force
}

function Assert-PackagedPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,
        [Parameter(Mandatory = $true)]
        [string]$RelativePath
    )

    $fullPath = Join-Path $Root $RelativePath
    if (-not (Test-Path $fullPath)) {
        throw "Missing required packaged path: $RelativePath"
    }
}

$resolvedSource = (Resolve-Path $SourcePackagePath).Path
$resolvedArtifacts = (Resolve-Path $ArtifactsPath).Path

if (Test-Path $OutputPath) {
    Remove-Item -Recurse -Force $OutputPath
}

New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null
Copy-DirectoryContent -Source $resolvedSource -Destination $OutputPath

$targets = @(
    @{ Artifact = "patina-server-x86_64-win"; PluginDir = "x86_64-win"; Binary = "patina-server.exe" },
    @{ Artifact = "patina-server-x86_64-linux"; PluginDir = "x86_64-linux"; Binary = "patina-server" },
    @{ Artifact = "patina-server-x86_64-macos"; PluginDir = "x86_64-macos"; Binary = "patina-server" },
    @{ Artifact = "patina-server-aarch64-macos"; PluginDir = "aarch64-macos"; Binary = "patina-server" }
)

foreach ($target in $targets) {
    $artifactBinary = Join-Path (Join-Path $resolvedArtifacts $target.Artifact) $target.Binary
    if (-not (Test-Path $artifactBinary)) {
        throw "Missing binary artifact: $artifactBinary"
    }

    $pluginTargetDir = Join-Path $OutputPath "Plugins/$($target.PluginDir)"
    New-Item -ItemType Directory -Force -Path $pluginTargetDir | Out-Null
    Copy-Item -Path $artifactBinary -Destination (Join-Path $pluginTargetDir $target.Binary) -Force
}

$manifestPath = Join-Path $OutputPath "package.json"
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$manifest.version = $Version
$manifest | ConvertTo-Json -Depth 10 | Set-Content -Path $manifestPath -Encoding utf8

$requiredPaths = @(
    "package.json",
    "Editor",
    "Plugins",
    "Editor/Patina.Editor.asmdef",
    "Plugins/x86_64-win/patina-server.exe",
    "Plugins/x86_64-linux/patina-server",
    "Plugins/x86_64-macos/patina-server",
    "Plugins/aarch64-macos/patina-server"
)

foreach ($requiredPath in $requiredPaths) {
    Assert-PackagedPath -Root $OutputPath -RelativePath $requiredPath
}

Write-Host "UPM package staged at $OutputPath"
Write-Host "Validated package version $Version with all required platform binaries present."
