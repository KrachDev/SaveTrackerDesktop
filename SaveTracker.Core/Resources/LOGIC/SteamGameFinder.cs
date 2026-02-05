using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SaveTracker.Resources.HELPERS;

namespace SaveTracker.Resources.LOGIC
{
    /// <summary>
    /// Finds and identifies game executables in Steam library folders
    /// </summary>
    public class SteamGameFinder
    {
        private static readonly HashSet<string> LauncherPatterns = new(StringComparer.OrdinalIgnoreCase)
        {
            "launcher", "bootstrap", "wrapper", "setup", "config"
        };

        private static readonly HashSet<string> ExcludedExePatterns = new(StringComparer.OrdinalIgnoreCase)
        {
            "uninstall", "dxwebsetup", "vcredist", "redist", "dotnet", "physx",
            "crash", "ue4prereq", "directx", "_win", "helper", "editor", "tool"
        };

        public SteamGameFinder()
        {
        }

        /// <summary>
        /// Finds executable paths for a Steam game given its install directory
        /// </summary>
        public SteamExecutableInfo FindGameExecutable(string installDirectory, string gameName = "")
        {
            var info = new SteamExecutableInfo();

            try
            {
                if (!Directory.Exists(installDirectory))
                {
                    DebugConsole.WriteWarning($"Install directory does not exist: {installDirectory}");
                    return info;
                }

                // Find all .exe files in the directory (non-recursive initially)
                var exeFiles = Directory.GetFiles(installDirectory, "*.exe", SearchOption.TopDirectoryOnly)
                    .Where(f => !IsExcludedExecutable(Path.GetFileName(f)))
                    .ToList();

                if (exeFiles.Count == 0)
                {
                    // Fallback: search recursively but limit depth
                    exeFiles = FindExecutablesRecursive(installDirectory, 2);
                }

                if (exeFiles.Count == 0)
                {
                    DebugConsole.WriteWarning($"No suitable executables found in {installDirectory}");
                    return info;
                }

                // Score and rank executables
                var scoredExes = ScoreExecutables(exeFiles, gameName, installDirectory);

                if (scoredExes.Count > 0)
                {
                    // Primary executable is the highest scored
                    info.PrimaryExecutable = scoredExes[0].Path;

                    // Alternative executables are the rest
                    if (scoredExes.Count > 1)
                    {
                        info.AlternativeExecutables = scoredExes.Skip(1)
                            .Select(e => e.Path)
                            .ToList();
                    }

                    DebugConsole.WriteSuccess(
                        $"Found executable for {gameName}: {Path.GetFileName(info.PrimaryExecutable)}" +
                        (info.AlternativeExecutables.Count > 0 ? $" (+{info.AlternativeExecutables.Count} alternatives)" : "")
                    );
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Error finding executable in {installDirectory}: {ex.Message}");
            }

            return info;
        }

        /// <summary>
        /// Scores executables based on heuristics to identify the main game executable
        /// </summary>
        private List<(string Path, double Score)> ScoreExecutables(List<string> exeFiles, string gameName, string installDir)
        {
            var scored = new List<(string Path, double Score)>();

            foreach (var exePath in exeFiles)
            {
                double score = 0;
                string fileName = Path.GetFileName(exePath).ToLower();
                string nameWithoutExt = Path.GetFileNameWithoutExtension(exePath).ToLower();

                // Score 1: File size (larger files are usually main executables, not utilities)
                try
                {
                    long fileSize = new FileInfo(exePath).Length;
                    // Normal game exe: 500 KB - 2000 MB. Penalize very small or very large files
                    if (fileSize > 500000 && fileSize < 2000000000)
                    {
                        score += 20 + (Math.Log(fileSize) / 10); // Logarithmic scale
                    }
                    else if (fileSize < 500000)
                    {
                        score -= 10; // Likely a launcher/tool
                    }
                }
                catch { }

                // Score 2: Filename matches game name (game.exe is main, launcher.exe is not)
                if (!string.IsNullOrEmpty(gameName))
                {
                    string normalizedGameName = Regex.Replace(gameName, @"[^a-z0-9]", "").ToLower();
                    string normalizedFileName = Regex.Replace(nameWithoutExt, @"[^a-z0-9]", "").ToLower();

                    if (normalizedFileName == normalizedGameName)
                    {
                        score += 50; // High priority if exact name match
                    }
                    else if (normalizedFileName.Contains(normalizedGameName) || normalizedGameName.Contains(normalizedFileName))
                    {
                        score += 30; // Medium priority if partial match
                    }
                }

                // Score 3: Location (root > subdirectories)
                int depth = exePath.Count(c => c == Path.DirectorySeparatorChar) - 
                           installDir.Count(c => c == Path.DirectorySeparatorChar);
                score -= depth * 5; // Penalize deeply nested executables

                // Score 4: Avoid known launcher patterns
                if (IsLauncherExecutable(fileName))
                {
                    score -= 25;
                }

                // Score 5: Prefer common game exe names
                if (IsCommonGameExecutable(fileName))
                {
                    score += 15;
                }

                scored.Add((exePath, score));
            }

            // Sort by score descending
            return scored.OrderByDescending(e => e.Score).ToList();
        }

        /// <summary>
        /// Checks if an executable is likely a launcher/wrapper rather than the game
        /// </summary>
        private bool IsLauncherExecutable(string fileName)
        {
            string nameLower = fileName.ToLower();

            foreach (var pattern in LauncherPatterns)
            {
                if (nameLower.Contains(pattern))
                    return true;
            }

            // Check for common launcher patterns
            if (nameLower.Contains("epicgameslauncher") || nameLower.Contains("origin") ||
                nameLower.Contains("battlenet") || nameLower.Contains("uplay"))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if an executable should be excluded from search
        /// </summary>
        private bool IsExcludedExecutable(string fileName)
        {
            string nameLower = fileName.ToLower();

            foreach (var pattern in ExcludedExePatterns)
            {
                if (nameLower.Contains(pattern))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if filename looks like a typical game executable
        /// </summary>
        private bool IsCommonGameExecutable(string fileName)
        {
            string nameLower = fileName.ToLower();

            // Common main game exe names
            var commonNames = new[] { "game.exe", "app.exe", "main.exe", "bin.exe" };
            if (commonNames.Contains(nameLower))
                return true;

            // Avoid common non-game patterns
            if (nameLower.StartsWith("vc_") || nameLower.StartsWith("dot_"))
                return false;

            return true;
        }

        /// <summary>
        /// Recursively finds executables up to a specified depth
        /// </summary>
        private List<string> FindExecutablesRecursive(string directory, int maxDepth)
        {
            var exeFiles = new List<string>();

            try
            {
                if (maxDepth <= 0)
                    return exeFiles;

                var files = Directory.GetFiles(directory, "*.exe", SearchOption.TopDirectoryOnly)
                    .Where(f => !IsExcludedExecutable(Path.GetFileName(f)))
                    .ToList();

                exeFiles.AddRange(files);

                // Search subdirectories
                if (maxDepth > 1)
                {
                    var subdirs = Directory.GetDirectories(directory);
                    foreach (var subdir in subdirs)
                    {
                        try
                        {
                            // Skip system/cache directories
                            string dirName = Path.GetFileName(subdir).ToLower();
                            if (dirName == "windows" || dirName == ".git" || dirName == "redist" ||
                                dirName == "support" || dirName == "tools" || dirName == "cache")
                            {
                                continue;
                            }

                            exeFiles.AddRange(FindExecutablesRecursive(subdir, maxDepth - 1));
                        }
                        catch { }
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                DebugConsole.WriteWarning($"Access denied to directory: {directory}");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Error searching directory {directory}: {ex.Message}");
            }

            return exeFiles;
        }
    }

    /// <summary>
    /// Information about found Steam game executables
    /// </summary>
    public class SteamExecutableInfo
    {
        public string PrimaryExecutable { get; set; } = "";
        public List<string> AlternativeExecutables { get; set; } = new();

        public bool HasExecutable => !string.IsNullOrEmpty(PrimaryExecutable);

        public override string ToString()
        {
            if (!HasExecutable)
                return "No executable found";

            return $"{Path.GetFileName(PrimaryExecutable)}" +
                   (AlternativeExecutables.Count > 0 ? $" (+{AlternativeExecutables.Count})" : "");
        }
    }
}
