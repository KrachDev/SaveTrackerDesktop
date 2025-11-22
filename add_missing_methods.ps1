$filePath = "SaveTracker\ViewModels\MainWindowViewModel.cs"
$content = Get-Content $filePath -Raw

# Add OnSettingsRequested event after OnRcloneSetupRequired
$content = $content -replace 'public event Action\? OnRcloneSetupRequired;', @'
public event Action? OnRcloneSetupRequired;
        public event Action? OnSettingsRequested;
'@

# Add ReloadConfigAsync method after LoadDataAsync
$pattern = '(private async Task LoadDataAsync\(\)[\s\S]*?^\s{8}\})'
$replacement = @'
$1

        public async Task ReloadConfigAsync()
        {
            await LoadDataAsync();
        }
'@

$content = $content -replace $pattern, $replacement

Set-Content -Path $filePath -Value $content -NoNewline
Write-Host "Added OnSettingsRequested event and ReloadConfigAsync method"
