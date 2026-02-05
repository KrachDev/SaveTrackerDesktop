using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SaveTracker.Resources.HELPERS.Linux
{
    public class HeroicGameInfo
    {
        public string AppId { get; set; } = "";
        public string WinePrefix { get; set; } = "";
    }

    public static class HeroicLibraryParser
    {
        public static HeroicGameInfo? FindGameInfo(string gameTitle)
        {
            try
            {
                var configDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); // ~/.config
                var heroicBase = Path.Combine(configDir, "heroic");
                var storeDir = Path.Combine(heroicBase, "store");
                var configPath = Path.Combine(storeDir, "config.json");

                // 1. Get Global Default Prefix
                string globalPrefix = "";
                if (File.Exists(configPath))
                {
                    try
                    {
                        var root = JsonNode.Parse(File.ReadAllText(configPath));
                        globalPrefix = root?["settings"]?["winePrefix"]?.ToString() ?? "";
                    }
                    catch { }
                }

                // 2. Check library.json (Full library usually)
                var libraryPath = Path.Combine(storeDir, "library.json");
                if (File.Exists(libraryPath))
                {
                    var info = ParseJsonForInfo(libraryPath, gameTitle, isLibrary: true, globalPrefix);
                    if (info != null) return info;
                }

                // 3. Check config.json (Recent games)
                if (File.Exists(configPath))
                {
                    var info = ParseJsonForInfo(configPath, gameTitle, isLibrary: false, globalPrefix);
                    if (info != null) return info;
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Failed to parse Heroic config: {ex.Message}");
            }

            return null;
        }

        // Backwards compatibility if needed, or just remove
        public static string? FindGameId(string gameTitle) => FindGameInfo(gameTitle)?.AppId;

        private static HeroicGameInfo? ParseJsonForInfo(string path, string targetTitle, bool isLibrary, string globalPrefix)
        {
            try
            {
                var jsonString = File.ReadAllText(path);
                var root = JsonNode.Parse(jsonString);

                if (root == null) return null;

                if (isLibrary)
                {
                    if (root is JsonArray arr)
                    {
                        return SearchInArray(arr, targetTitle, globalPrefix);
                    }
                }
                else
                {
                    var recent = root["games"]?["recent"]?.AsArray();
                    if (recent != null)
                    {
                        return SearchInArray(recent, targetTitle, globalPrefix);
                    }
                }
            }
            catch
            {
                // Ignore parsing errors
            }
            return null;
        }

        private static HeroicGameInfo? SearchInArray(JsonArray array, string targetTitle, string globalPrefix)
        {
            var candidates = new List<(HeroicGameInfo Info, string Title, string AppName)>();

            // Collect all potential nodes to avoid double iteration performance hit? 
            // Actually config is small, two iterations is fine.

            // Pass 1: Exact Sanitized Match
            foreach (var node in array)
            {
                if (node == null) continue;
                var title = node["title"]?.ToString();
                var appName = node["appName"]?.ToString();

                if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(appName))
                {
                    if (IsExactMatch(title, targetTitle))
                    {
                        DebugConsole.WriteInfo($"[HeroicParser] Exact match found: '{title}' ({appName})");
                        return BuildInfo(node, title, appName, globalPrefix);
                    }
                }
            }

            // Pass 2: Partial Match (Heroic title contains Target title)
            // e.g. Target: "Witcher 3", Heroic: "The Witcher 3: Wild Hunt"
            string sanitizedTarget = Sanitize(targetTitle);
            foreach (var node in array)
            {
                if (node == null) continue;
                var title = node["title"]?.ToString();
                var appName = node["appName"]?.ToString();

                if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(appName))
                {
                    string sanitizedHeroic = Sanitize(title);
                    if (sanitizedHeroic.Contains(sanitizedTarget))
                    {
                        DebugConsole.WriteInfo($"[HeroicParser] Partial match found: '{title}' contains '{targetTitle}'");
                        return BuildInfo(node, title, appName, globalPrefix);
                    }
                }
            }

            DebugConsole.WriteWarning($"[HeroicParser] No match found for '{targetTitle}'");
            return null;
        }

        private static HeroicGameInfo BuildInfo(JsonNode node, string title, string appName, string globalPrefix)
        {
            var info = new HeroicGameInfo { AppId = appName };

            // Try to find specific config
            var heroicBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "heroic");
            var gameConfigPath = Path.Combine(heroicBase, "GamesConfig", $"{appName}.json");

            // 1. Try Game Specific Config
            if (File.Exists(gameConfigPath))
            {
                try
                {
                    var gameConfigStr = File.ReadAllText(gameConfigPath);
                    var gameConfig = JsonNode.Parse(gameConfigStr);
                    info.WinePrefix = gameConfig?["winePrefix"]?.ToString() ?? "";
                }
                catch { }
            }

            // 2. Fallback to global prefix if empty
            if (string.IsNullOrEmpty(info.WinePrefix))
            {
                info.WinePrefix = globalPrefix;
            }

            // 3. Validation and Refinement
            if (!string.IsNullOrEmpty(info.WinePrefix))
            {
                string driveCPath = Path.Combine(info.WinePrefix, "drive_c");
                if (!Directory.Exists(driveCPath))
                {
                    // Check for Game Title Subfolder
                    string titleSub = Path.Combine(info.WinePrefix, title);
                    if (Directory.Exists(Path.Combine(titleSub, "drive_c")))
                    {
                        info.WinePrefix = titleSub;
                    }
                    else
                    {
                        // Check for App ID Subfolder
                        string idSub = Path.Combine(info.WinePrefix, appName);
                        if (Directory.Exists(Path.Combine(idSub, "drive_c")))
                        {
                            info.WinePrefix = idSub;
                        }
                    }
                }
            }
            return info;
        }

        private static bool IsExactMatch(string title, string targetTitle)
        {
            if (title.Equals(targetTitle, StringComparison.OrdinalIgnoreCase)) return true;
            return Sanitize(title) == Sanitize(targetTitle);
        }

        private static string Sanitize(string input)
        {
            // Remove everything except letters and numbers
            return System.Text.RegularExpressions.Regex.Replace(input, "[^a-zA-Z0-9]", "").ToLowerInvariant();
        }
    }
}
