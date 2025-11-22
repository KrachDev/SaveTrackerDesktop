$filePath = "SaveTracker\ViewModels\MainWindowViewModel.cs"
$lines = Get-Content $filePath

# Find the line after LoadDataAsync closes (look for the closing brace followed by blank line and "partial void")
$insertLine = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^\s+partial void OnSelectedGameChanged') {
        $insertLine = $i
        break
    }
}

if ($insertLine -eq -1) {
    Write-Host "Could not find insertion point"
    exit 1
}

# Create the new method
$newMethod = @(
    "        public async Task ReloadConfigAsync()",
    "        {",
    "            await LoadDataAsync();",
    "        }",
    ""
)

# Insert the method
$newContent = @()
$newContent += $lines[0..($insertLine - 1)]
$newContent += $newMethod
$newContent += $lines[$insertLine..($lines.Count - 1)]

# Write back
$newContent | Set-Content $filePath -Encoding UTF8
Write-Host "Added ReloadConfigAsync method at line $insertLine"
