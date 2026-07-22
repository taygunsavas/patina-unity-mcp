param(
    [string]$BaseRef = ""
)

$ErrorActionPreference = "Stop"

function Test-GeneratedCSharpFile([string]$Path) {
    $normalized = $Path.Replace('\', '/')
    return $normalized -match '(^|/)(bin|obj)/' -or
        $normalized -match '\.(g|generated|designer)\.cs$'
}

if ([string]::IsNullOrWhiteSpace($BaseRef) -or -not (git rev-parse --verify "$BaseRef^{commit}" 2>$null)) {
    if (git rev-parse --verify 'main^{commit}' 2>$null) { $BaseRef = 'main' }
    elseif (git rev-parse --verify 'origin/main^{commit}' 2>$null) { $BaseRef = 'origin/main' }
    elseif (git rev-parse --verify 'HEAD~1^{commit}' 2>$null) { $BaseRef = 'HEAD~1' }
    else { $BaseRef = 'HEAD' }
}

$files = @()
if ($BaseRef -ne 'HEAD') {
    $files += @(git diff --name-only --diff-filter=ACMR "$BaseRef...HEAD" -- '*.cs')
}
$files += @(git diff --name-only --diff-filter=ACMR --cached -- '*.cs')
$files += @(git diff --name-only --diff-filter=ACMR -- '*.cs')
$files += @(git ls-files --others --exclude-standard -- '*.cs')

$files |
    Sort-Object -Unique |
    Where-Object { $_ -and (Test-Path $_) -and -not (Test-GeneratedCSharpFile $_) }
