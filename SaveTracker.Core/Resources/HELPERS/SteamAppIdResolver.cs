using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using SaveTracker.Resources.HELPERS;

namespace SaveTracker.Resources.HELPERS
{
    /// <summary>
    /// Resolves Steam App IDs from game names using Steam's API
    /// </summary>
    public static class SteamAppIdResolver
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private static readonly Dictionary<string, string> _cachedAppIds = new();

        /// <summary>
        /// Attempts to resolve a Steam App ID from a game name
        /// </summary>
        public static async Task<string?> ResolveAppIdAsync(string gameName)
        {
            if (string.IsNullOrWhiteSpace(gameName))
                return null;

            // Check cache first
            if (_cachedAppIds.TryGetValue(gameName, out var cachedId))
            {
                DebugConsole.WriteDebug($"[SteamResolver] Using cached App ID for {gameName}: {cachedId}");
                return cachedId;
            }

            try
            {
                // Query Steam API
                string query = Uri.EscapeDataString(gameName);
                string url = $"https://steamcommunity.com/api/ISteamApps/GetAppList/v2/";

                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("applist", out var appList) &&
                    appList.TryGetProperty("apps", out var apps))
                {
                    // Search for exact match first
                    var exactMatch = apps.EnumerateArray()
                        .FirstOrDefault(app =>
                            app.TryGetProperty("name", out var nameElement) &&
                            nameElement.GetString()?.Equals(gameName, StringComparison.OrdinalIgnoreCase) == true);

                    if (exactMatch.TryGetProperty("appid", out var appId))
                    {
                        string id = appId.GetInt32().ToString();
                        _cachedAppIds[gameName] = id;
                        DebugConsole.WriteInfo($"[SteamResolver] Resolved {gameName} -> {id}");
                        return id;
                    }

                    // Fallback: Fuzzy match (contains game name)
                    var fuzzyMatch = apps.EnumerateArray()
                        .FirstOrDefault(app =>
                            app.TryGetProperty("name", out var nameElement) &&
                            nameElement.GetString()?.Contains(gameName, StringComparison.OrdinalIgnoreCase) == true);

                    if (fuzzyMatch.TryGetProperty("appid", out var fuzzyAppId))
                    {
                        string id = fuzzyAppId.GetInt32().ToString();
                        _cachedAppIds[gameName] = id;
                        DebugConsole.WriteDebug($"[SteamResolver] Fuzzy matched {gameName} -> {id}");
                        return id;
                    }
                }

                DebugConsole.WriteDebug($"[SteamResolver] No Steam App ID found for: {gameName}");
                return null;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteDebug($"[SteamResolver] Failed to resolve App ID for {gameName}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Common game names that are hard to match - add more as needed
        /// </summary>
        private static readonly Dictionary<string, string> ManualMappings = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Crime Scene Cleaner", "1254770" },
            { "Euro Truck Simulator 2", "227300" },
            { "Expedition33_ Steam", "1903340" },
            { "Little Nightmares III", "1808370" },
            { "Little Nightmares I I I", "1808370" }, // Alternative spelling
            { "Marvel's Spider-Man_ Miles Morales", "1817070" },
            { "Moonlighter", "629730" },
            { "Taxi Life - A City Driving Simulator", "2938310" },
            { "EA SPORTS FC 26", "2054960" },
            { "God of War Ragnarök", "2322010" },
            { "The Witcher 3", "292030" },
            // Add more as you discover unmatchable games
        };

        /// <summary>
        /// Gets App ID from manual mapping if available
        /// </summary>
        public static string? GetFromManualMapping(string gameName)
        {
            if (ManualMappings.TryGetValue(gameName, out var appId))
            {
                DebugConsole.WriteDebug($"[SteamResolver] Using manual mapping for {gameName}: {appId}");
                return appId;
            }
            return null;
        }
    }
}
