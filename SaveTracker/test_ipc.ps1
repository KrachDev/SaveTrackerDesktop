$pipeName = "SaveTracker_Command_Pipe"
$pipePath = "\\.\pipe\$pipeName"

function Send-IpcCommand {
    param(
        [string]$json
    )

    try {
        $client = New-Object System.IO.Pipes.NamedPipeClientStream(".", $pipeName, [System.IO.Pipes.PipeDirection]::InOut)
        $client.Connect(5000) # Wait up to 5s
        
        $writer = New-Object System.IO.StreamWriter($client)
        $writer.AutoFlush = $true
        $reader = New-Object System.IO.StreamReader($client)

        $writer.WriteLine($json)
        $response = $reader.ReadLine()
        
        return $response
    }
    catch {
        return "Error: $_"
    }
    finally {
        if ($client) { $client.Dispose() }
    }
}

Write-Host "Testing Ping..."
$ping = Send-IpcCommand '{"cmd":"Ping"}'
Write-Host "Ping: $ping"

Write-Host "Testing IsTracking..."
$tracking = Send-IpcCommand '{"cmd":"IsTracking"}'
Write-Host "IsTracking: $tracking"

Write-Host "Testing GetGameList (FIRST 100 chars)..."
$games = Send-IpcCommand '{"cmd":"GetGameList"}'
if ($games.Length -gt 100) {
    Write-Host "Games: $($games.Substring(0, 100))..."
}
else {
    Write-Host "Games: $games"
}

Write-Host "Testing ShowLibrary..."
$show = Send-IpcCommand '{"cmd":"ShowLibrary"}'
Write-Host "ShowLibrary: $show"

