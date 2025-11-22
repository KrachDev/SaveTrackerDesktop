$filePath = "SaveTracker\ViewModels\MainWindowViewModel.cs"
$lines = Get-Content $filePath

# 1. Add game filtering in LoadDataAsync
$insertAfterLine = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'foreach \(var game in gamelist\)') {
        $insertAfterLine = $i + 1
        break
    }
}

$filterLines = @(
    "                        // Skip deleted games - don't load them into the UI",
    "                        if (game.IsDeleted)",
    "                        {",
    "                            DebugConsole.WriteInfo(`$`"Skipping deleted game: {game.Name}`");",
    "                            continue;",
    "                        }",
    ""
)

$newContent = @()
$newContent += $lines[0..$insertAfterLine]
$newContent += $filterLines
$newContent += $lines[($insertAfterLine + 1)..($lines.Count - 1)]
$lines = $newContent

# 2. Add OnSettingsRequested event
$content = $lines -join "`r`n"
$content = $content -replace 'public event Action\? OnRcloneSetupRequired;', @'
public event Action? OnRcloneSetupRequired;
        public event Action? OnSettingsRequested;
'@
$lines = $content -split "`r`n"

# 3. Add ReloadConfigAsync method (after LoadDataAsync closes)
$insertLine = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^\s+partial void OnSelectedGameChanged') {
        $insertLine = $i
        break
    }
}

$reloadMethod = @(
    "        public async Task ReloadConfigAsync()",
    "        {",
    "            await LoadDataAsync();",
    "        }",
    ""
)

$newContent = @()
$newContent += $lines[0..($insertLine - 1)]
$newContent += $reloadMethod
$newContent += $lines[$insertLine..($lines.Count - 1)]
$lines = $newContent

# 4. Add OpenSettings command (after OpenBlacklistAsync)
$insertLine = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'private async Task OpenBlacklistAsync') {
        # Find the closing brace of this method
        $braceCount = 0
        for ($j = $i; $j -lt $lines.Count; $j++) {
            if ($lines[$j] -match '\{') { $braceCount++ }
            if ($lines[$j] -match '\}') { 
                $braceCount--
                if ($braceCount == 0) {
                    $insertLine = $j + 1
                    break
                }
            }
        }
        break
    }
}

$settingsCommand = @(
    "",
    "        [RelayCommand]",
    "        private void OpenSettings()",
    "        {",
    "            OnSettingsRequested?.Invoke();",
    "        }"
)

$newContent = @()
$newContent += $lines[0..($insertLine - 1)]
$newContent += $settingsCommand
$newContent += $lines[$insertLine..($lines.Count - 1)]

# Write back
$newContent | Set-Content $filePath -Encoding UTF8
Write-Host "All changes applied successfully!"
