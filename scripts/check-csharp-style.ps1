param(
    [string]$BaseRef = ""
)

$ErrorActionPreference = "Stop"
$files = @(& "$PSScriptRoot/Get-ChangedCSharpFiles.ps1" -BaseRef $BaseRef)
if ($files.Count -eq 0) {
    Write-Host "No changed C# files to style-check."
    exit 0
}

dotnet run --project "$PSScriptRoot/../tools/Patina.CSharpStyleCheck/Patina.CSharpStyleCheck.csproj" -- @files
