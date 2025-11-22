$filePath = "SaveTracker\ViewModels\MainWindowViewModel.cs"
$lines = Get-Content $filePath

# Insert after OpenBlacklistAsync (around line 846)
$insertLine = 846

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
Write-Host "Added OpenSettings command after line $insertLine"
