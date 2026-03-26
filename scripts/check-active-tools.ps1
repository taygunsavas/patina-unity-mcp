param(
    [string]$BinaryPath = "rust-server/target/release/patina-server.exe",
    [int]$Port = 9800
)

$ErrorActionPreference = "Stop"

function Read-ExpectedToolNames {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServerPath
    )

    $matches = Select-String -Path $ServerPath -Pattern '^\s*name\s*=\s*"([^"]+)"' -AllMatches
    $names = New-Object System.Collections.Generic.List[string]
    foreach ($match in $matches) {
        foreach ($capture in $match.Matches) {
            $names.Add($capture.Groups[1].Value)
        }
    }

    return $names | Sort-Object -Unique
}

function Write-McpMessage {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.StreamWriter]$Writer,
        [Parameter(Mandatory = $true)]
        [hashtable]$Payload
    )

    $json = $Payload | ConvertTo-Json -Depth 20 -Compress
    $Writer.WriteLine($json)
    $Writer.Flush()
}

function Read-McpMessage {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.StreamReader]$Reader
    )

    $json = $Reader.ReadLine()
    if ([string]::IsNullOrWhiteSpace($json)) {
        throw "Unexpected end of stream before receiving an MCP message."
    }

    return $json | ConvertFrom-Json
}

function Wait-ForResponse {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.StreamReader]$Reader,
        [Parameter(Mandatory = $true)]
        [string]$Id
    )

    while ($true) {
        $message = Read-McpMessage -Reader $Reader
        if ($null -ne $message.id -and [string]$message.id -eq $Id) {
            return $message
        }
    }
}

$resolvedBinary = (Resolve-Path $BinaryPath).Path
$expectedToolNames = Read-ExpectedToolNames -ServerPath "rust-server/src/server.rs"

$process = New-Object System.Diagnostics.Process
$process.StartInfo = New-Object System.Diagnostics.ProcessStartInfo
$process.StartInfo.FileName = $resolvedBinary
$process.StartInfo.Arguments = "--port $Port"
$process.StartInfo.UseShellExecute = $false
$process.StartInfo.RedirectStandardInput = $true
$process.StartInfo.RedirectStandardOutput = $true
$process.StartInfo.RedirectStandardError = $true
$process.StartInfo.CreateNoWindow = $true

if (-not $process.Start()) {
    throw "Failed to start Patina server binary."
}

try {
    $inputWriter = $process.StandardInput
    $outputReader = $process.StandardOutput

    Write-McpMessage -Writer $inputWriter -Payload @{
        jsonrpc = "2.0"
        id = "initialize"
        method = "initialize"
        params = @{
            protocolVersion = "2024-11-05"
            capabilities = @{}
            clientInfo = @{
                name = "patina-tool-check"
                version = "1.0.0"
            }
        }
    }

    $initializeResponse = Wait-ForResponse -Reader $outputReader -Id "initialize"
    if ($null -eq $initializeResponse.result) {
        throw "Initialize failed: $($initializeResponse | ConvertTo-Json -Depth 20 -Compress)"
    }

    Write-McpMessage -Writer $inputWriter -Payload @{
        jsonrpc = "2.0"
        method = "notifications/initialized"
        params = @{}
    }

    Write-McpMessage -Writer $inputWriter -Payload @{
        jsonrpc = "2.0"
        id = "tools-list"
        method = "tools/list"
        params = @{}
    }

    $toolsResponse = Wait-ForResponse -Reader $outputReader -Id "tools-list"
    if ($null -eq $toolsResponse.result) {
        throw "tools/list failed: $($toolsResponse | ConvertTo-Json -Depth 20 -Compress)"
    }

    $actualToolNames = @($toolsResponse.result.tools | ForEach-Object { $_.name } | Sort-Object -Unique)
    $missing = @($expectedToolNames | Where-Object { $_ -notin $actualToolNames })
    $extra = @($actualToolNames | Where-Object { $_ -notin $expectedToolNames })

    Write-Host "Binary        : $resolvedBinary"
    Write-Host "Expected tools: $($expectedToolNames.Count)"
    Write-Host "Actual tools  : $($actualToolNames.Count)"
    Write-Host "Tool names    : $($actualToolNames -join ', ')"

    if ($missing.Count -gt 0 -or $extra.Count -gt 0) {
        if ($missing.Count -gt 0) {
            Write-Host "Missing tools : $($missing -join ', ')"
        }

        if ($extra.Count -gt 0) {
            Write-Host "Extra tools   : $($extra -join ', ')"
        }

        throw "Active runtime tool set does not match source definitions."
    }
}
finally {
    if (-not $process.HasExited) {
        $process.Kill()
        $process.WaitForExit()
    }

    $stderrOutput = $process.StandardError.ReadToEnd()
    if (-not [string]::IsNullOrWhiteSpace($stderrOutput)) {
        Write-Host "stderr:"
        Write-Host $stderrOutput.Trim()
    }
}
