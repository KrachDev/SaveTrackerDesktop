$filePath = "SaveTracker\ViewModels\MainWindowViewModel.cs"
$lines = Get-Content $filePath

# Find the line with "foreach (var game in gamelist)"
$insertAfterLine = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'foreach \(var game in gamelist\)') {
        $insertAfterLine = $i + 1  # After the opening brace
        break
    }
}

if ($insertAfterLine -eq -1) {
    Write-Host "Pattern not found!"
    exit 1
}

# Create the new lines to insert
$newLines = @(
    "                        // Skip deleted games - don't load them into the UI",
    "                        if (game.IsDeleted)",
    "                        {",
    "                            DebugConsole.WriteInfo(`$`"Skipping deleted game: {game.Name}`");",
    "                            continue;",
    "                        }",
    ""
)

# Insert the new lines
$newContent = @()
$newContent += $lines[0..$insertAfterLine]
$newContent += $newLines
$newContent += $lines[($insertAfterLine + 1)..($lines.Count - 1)]

# Write back to file
$newContent | Set-Content $filePath -Encoding UTF8
Write-Host "Successfully added filtering code at line $($insertAfterLine + 1)"
