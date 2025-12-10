using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SaveTracker.Resources.SAVE_SYSTEM;

namespace SaveTracker.Resources.HELPERS
{
    /// <summary>
    /// Collects and manages tracked files during a session
    /// </summary>
    public class FileCollector
    {
        private readonly Game _game;
        private readonly HashSet<string> _collectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new object();
        private static readonly HashSet<string> _loggedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public FileCollector(Game game)
        {
            _game = game ?? throw new ArgumentNullException(nameof(game));
        }

        /// <summary>
        /// Determines if a file path should be ignored using the original logic
        /// </summary>
        public bool ShouldIgnore(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return true;

            // Normalize the path for consistent tracking
            // Use Path.DirectorySeparatorChar to ensure we match the OS
            string normalizedPath = filePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            bool shouldLog = _loggedFiles.Add(normalizedPath); // Returns false if already exists


            // 1. Game-specific blacklist check (highest priority)
            if (_game != null)
            {
                try
                {
                    var data = ConfigManagement.GetGameData(_game).Result;
                    if (data?.Blacklist != null && data.Blacklist.Count > 0)
                    {
                        foreach (var blacklistItem in data.Blacklist)
                        {
                            // Get the path from the FileChecksumRecord
                            string blacklistPath = blacklistItem.Value?.Path;
                            if (string.IsNullOrEmpty(blacklistPath))
                                continue;

                            // Normalize blacklist path once per iteration
                            string normalizedBlacklist = blacklistPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

                            // Combined exact path match
                            if (string.Equals(normalizedPath, normalizedBlacklist, StringComparison.OrdinalIgnoreCase))
                            {
                                if (shouldLog)
                                    DebugConsole.WriteWarning($"Skipped (Game Blacklist - Exact): {filePath}");
                                return true;
                            }

                            // Check if file is within blacklisted directory
                            if (normalizedPath.StartsWith(normalizedBlacklist + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                            {
                                if (shouldLog)
                                    DebugConsole.WriteWarning($"Skipped (Game Blacklist - Directory): {filePath}");
                                return true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Silently continue if blacklist check fails
                    DebugConsole.WriteWarning($"Blacklist check failed: {ex.Message}");
                }
            }

            try
            {
                // 2. Quick directory check - most performant filter
                foreach (var ignoredDir in Ignorlist.IgnoredDirectoriesSet)
                {
                    if (normalizedPath.StartsWith(ignoredDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                        normalizedPath.Equals(ignoredDir, StringComparison.OrdinalIgnoreCase))
                    {
                        if (shouldLog)
                            DebugConsole.WriteWarning($"Skipped (System Directory): {filePath}");
                        return true;
                    }
                }

                // 3. File name and extension checks
                string fileName = Path.GetFileName(normalizedPath);
                string fileExtension = Path.GetExtension(normalizedPath);

                if (Ignorlist.IgnoredFileNames.Contains(fileName))
                {
                    if (shouldLog)
                        DebugConsole.WriteWarning($"Skipped (Ignored Filename): {filePath}");
                    return true;
                }

                if (Ignorlist.IgnoredExtensions.Contains(fileExtension))
                {
                    if (shouldLog)
                        DebugConsole.WriteWarning($"Skipped (Ignored Extension): {filePath}");
                    return true;
                }

                // 4. Simple keyword filtering
                string lowerPath = normalizedPath.ToLower();
                string lowerFileName = fileName.ToLower();

                foreach (var keyword in Ignorlist.IgnoredKeywords)
                {
                    if (lowerFileName.Contains(keyword))
                    {
                        if (shouldLog)
                            DebugConsole.WriteWarning($"Skipped (Keyword in Filename '{keyword}'): {filePath}");
                        return true;
                    }

                    if (lowerPath.Contains($"{Path.DirectorySeparatorChar}{keyword}{Path.DirectorySeparatorChar}"))
                    {
                        if (shouldLog)
                            DebugConsole.WriteWarning($"Skipped (Keyword in Path '{keyword}'): {filePath}");
                        return true;
                    }
                }

                // 5. System file heuristics
                if (IsObviousSystemFile(fileName))
                {
                    if (shouldLog)
                        DebugConsole.WriteWarning($"Skipped (System File Heuristic): {filePath}");
                    return true;
                }

                // File passed all filters
                _loggedFiles.Remove(normalizedPath);
                return false;
            }
            catch (Exception ex)
            {
                if (shouldLog)
                    DebugConsole.WriteWarning($"Skipped (Path Processing Error): {filePath} - {ex.Message}");
                return false;
            }
        }

        private static bool IsObviousSystemFile(string fileName)
        {
            // Files starting with ~ or . are usually temp/hidden
            if (fileName.StartsWith("~") || fileName.StartsWith("."))
                return true;
            return false;
        }

        /// <summary>
        /// Adds a file to the collection if it's not already tracked
        /// </summary>
        /// <returns>True if the file was newly added, false if already tracked</returns>
        public bool AddFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            lock (_lock)
            {
                return _collectedFiles.Add(filePath);
            }
        }

        /// <summary>
        /// Gets all collected files
        /// </summary>
        public List<string> GetCollectedFiles()
        {
            lock (_lock)
            {
                return new List<string>(_collectedFiles);
            }
        }

        /// <summary>
        /// Clears all collected files
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _collectedFiles.Clear();
            }
        }

        /// <summary>
        /// Gets the count of collected files
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _collectedFiles.Count;
                }
            }
        }
    }
}
