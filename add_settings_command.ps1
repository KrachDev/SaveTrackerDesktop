$filePath = "SaveTracker\ViewModels\MainWindowViewModel.cs"
$lines = Get-Content $filePath

# Find where to insert the command (after OpenBlacklist command)
$insertLine = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'private void OpenBlacklist\(\)') {
        # Find the closing brace
        for ($j = $i; $j -lt $lines.Count; $j++) {
            if ($lines[$j] -match '^\s+\}$') {
                $insertLine = $j + 1
                break
            }
        }
        break
    }
}

if ($insertLine -eq -1) {
    Write-Host "Could not find insertion point"
    exit 1
}

# Create the new command
$newCommand = @(
    "",
    "        [RelayCommand]",
    "        private void OpenSettings()",
    "        {",
    "            OnSettingsRequested?.Invoke();",
    "        }"
)

# Insert the command
$newContent = @()
$newContent += $lines[0..($insertLine - 1)]
$newContent += $newCommand
$newContent += $lines[$insertLine..($lines.Count - 1)]

# Write back
$newContent | Set-Content $filePath -Encoding UTF8
Write-Host "Added OpenSettings command at line $insertLine"
