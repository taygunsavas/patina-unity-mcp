param(
    [string]$BaseRef = ""
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($BaseRef)) {
    if (git rev-parse --verify main 2>$null) { $BaseRef = "main" }
    elseif (git rev-parse --verify origin/main 2>$null) { $BaseRef = "origin/main" }
    else { $BaseRef = "HEAD~1" }
}

$files = @(& "$PSScriptRoot/Get-ChangedCSharpFiles.ps1" -BaseRef $BaseRef)
if ($files.Count -eq 0) { Write-Host "No changed C# files to format-check."; exit 0 }

dotnet tool run csharpier -- check @files
