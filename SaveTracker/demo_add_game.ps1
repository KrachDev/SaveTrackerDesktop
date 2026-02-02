# IPC Demo: Add a Game and Check Status
$pipeName = "SaveTracker_Command_Pipe"

function Send-IpcCommand {
    param(
        [string]$json
    )

    $client = New-Object System.IO.Pipes.NamedPipeClientStream(".", $pipeName, [System.IO.Pipes.PipeDirection]::InOut)
    try {
        $client.Connect(500)
    }
    catch {
        Write-Error "Could not connect to SaveTracker. Is it running?"
        return $null
    }

    $writer = New-Object System.IO.StreamWriter($client)
    $writer.AutoFlush = $true
    $reader = New-Object System.IO.StreamReader($client)

    # Send
    $writer.Write($json)
    
    # Read Response (Server sends a single line)
    $response = $reader.ReadLine()
    
    $client.Dispose()
    return $response
}

# 1. Define the Game Object (JSON)
# We will add "Notepad" as a test game to demonstrate tracking.
# In a real scenario, this would be your game's exe.
$notepadData = @{
    cmd = "AddGame"
    params = @{
        Name = "Notepad Demo"
        ExecutablePath = "C:\Windows\System32\notepad.exe"
        InstallDirectory = "C:\Windows\System32"
        # Optional: Setup save paths if you wanted to track files
        # Directories = @( @{ Path = "..." } ) 
    }
} | ConvertTo-Json -Depth 5

Write-Host "--- 1. Adding Game: Notepad Demo ---"
$addResult = Send-IpcCommand $notepadData
Write-Host "Response: $addResult`n"

# 2. Verify it exists in the list
Write-Host "--- 2. Verifying Game List ---"
$listResult = Send-IpcCommand '{"cmd":"GetGameList"}'
# Simple string check for demo purposes
if ($listResult -match "Notepad Demo") {
    Write-Host "SUCCESS: 'Notepad Demo' found in game list."
} else {
    Write-Host "WARNING: Game not found in list."
}

Write-Host "`n--- 3. How Tracking Works ---"
Write-Host "Now that 'Notepad Demo' is added, SaveTracker's background watcher"
Write-Host "will automatically detect when 'notepad.exe' starts."
Write-Host "Try opening Notepad now, and check SaveTracker's status!"
