param(
    [string]$BinaryPath = "rust-server/target/release/patina-server.exe",
    [int]$Port = 9800
)

$ErrorActionPreference = "Stop"

function Read-ExpectedToolNames {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CatalogPath
    )

    $lines = Get-Content -Path $CatalogPath
    $names = New-Object System.Collections.Generic.List[string]
    $insideCommandMacro = $false
    foreach ($line in $lines) {
        if ($line -match 'command!\(') {
            $insideCommandMacro = $true
        }

        if ($insideCommandMacro -and $line -match '"([^"]+)"') {
            $names.Add($Matches[1])
            $insideCommandMacro = $false
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
$expectedCommandNames = Read-ExpectedToolNames -CatalogPath "rust-server/src/tools/catalog.rs"
$expectedAdvertisedTools = @("patina_call", "patina_capabilities", "patina_health", "patina_sessions") | Sort-Object -Unique

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
    $missingAdvertised = @($expectedAdvertisedTools | Where-Object { $_ -notin $actualToolNames })
    $extraAdvertised = @($actualToolNames | Where-Object { $_ -notin $expectedAdvertisedTools })

    Write-Host "Binary        : $resolvedBinary"
    Write-Host "Advertised expected tools: $($expectedAdvertisedTools.Count)"
    Write-Host "Advertised actual tools  : $($actualToolNames.Count)"
    Write-Host "Advertised tool names    : $($actualToolNames -join ', ')"

    if ($missingAdvertised.Count -gt 0 -or $extraAdvertised.Count -gt 0) {
        if ($missingAdvertised.Count -gt 0) {
            Write-Host "Missing advertised tools : $($missingAdvertised -join ', ')"
        }

        if ($extraAdvertised.Count -gt 0) {
            Write-Host "Extra advertised tools   : $($extraAdvertised -join ', ')"
        }

        throw "Active runtime advertised MCP tool set does not match compact surface definitions."
    }

    Write-McpMessage -Writer $inputWriter -Payload @{
        jsonrpc = "2.0"
        id = "capabilities"
        method = "tools/call"
        params = @{
            name = "patina_capabilities"
            arguments = @{}
        }
    }

    $capabilitiesResponse = Wait-ForResponse -Reader $outputReader -Id "capabilities"
    if ($null -eq $capabilitiesResponse.result) {
        throw "patina_capabilities failed: $($capabilitiesResponse | ConvertTo-Json -Depth 20 -Compress)"
    }

    $contentItem = @($capabilitiesResponse.result.content | Where-Object { $_.type -eq "text" } | Select-Object -First 1)
    if ($null -eq $contentItem -or [string]::IsNullOrWhiteSpace($contentItem.text)) {
        throw "patina_capabilities did not return text JSON: $($capabilitiesResponse | ConvertTo-Json -Depth 20 -Compress)"
    }

    $catalog = $contentItem.text | ConvertFrom-Json
    $actualCommandNames = @($catalog.commands | ForEach-Object { $_.name } | Sort-Object -Unique)
    $missingCommands = @($expectedCommandNames | Where-Object { $_ -notin $actualCommandNames })
    $extraCommands = @($actualCommandNames | Where-Object { $_ -notin $expectedCommandNames })

    Write-Host "Expected commands: $($expectedCommandNames.Count)"
    Write-Host "Actual commands  : $($actualCommandNames.Count)"
    Write-Host "Command names    : $($actualCommandNames -join ', ')"

    if ($missingCommands.Count -gt 0 -or $extraCommands.Count -gt 0) {
        if ($missingCommands.Count -gt 0) {
            Write-Host "Missing commands : $($missingCommands -join ', ')"
        }

        if ($extraCommands.Count -gt 0) {
            Write-Host "Extra commands   : $($extraCommands -join ', ')"
        }

        throw "Active runtime Patina command catalog does not match source definitions."
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
