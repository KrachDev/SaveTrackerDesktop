$filePath = "c:\Users\Krach\Documents\Projects\SaveTrackerDesktop\SaveTracker\ViewModels\MainWindowViewModel.cs"
$content = Get-Content $filePath -Raw

# Add the filtering logic after "foreach (var game in gamelist)"
$oldPattern = @'
                    foreach (var game in gamelist)
                    {
                        try
                        {
                            Games.Add(new GameViewModel(game));
                        }
'@

$newPattern = @'
                    foreach (var game in gamelist)
                    {
                        // Skip deleted games - don't load them into the UI
                        if (game.IsDeleted)
                        {
                            DebugConsole.WriteInfo($"Skipping deleted game: {game.Name}");
                            continue;
                        }

                        try
                        {
                            Games.Add(new GameViewModel(game));
                        }
'@

$content = $content -replace [regex]::Escape($oldPattern), $newPattern
Set-Content -Path $filePath -Value $content -NoNewline
Write-Host "File updated successfully"
