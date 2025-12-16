using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using SaveTracker.Resources.HELPERS;

namespace SaveTracker.Resources.LOGIC
{
    /// <summary>
    /// Handles moving conflicting/orphaned files to a safe quarantine location
    /// instead of overwriting them.
    /// </summary>
    public class QuarantineManager
    {
        private readonly string _gameDirectory;
        private readonly string _quarantineFolder;

        public QuarantineManager(string gameDirectory)
        {
            _gameDirectory = gameDirectory;
            _quarantineFolder = Path.Combine(gameDirectory, ".ST_QUARANTINE");
        }

        public void QuarantineFile(string filePath, string reason)
        {
            try
            {
                if (!Directory.Exists(_quarantineFolder))
                {
                    Directory.CreateDirectory(_quarantineFolder);
                    // Hide the quarantine folder
                    File.SetAttributes(_quarantineFolder, File.GetAttributes(_quarantineFolder) | FileAttributes.Hidden);
                }

                string fileName = Path.GetFileName(filePath);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string quarantineFilename = $"{timestamp}_{fileName}";
                string destPath = Path.Combine(_quarantineFolder, quarantineFilename);

                // Write metadata about why it was quarantined
                string metaPath = destPath + ".meta.txt";
                File.WriteAllText(metaPath, $"OriginalPath: {filePath}\nDate: {DateTime.Now}\nReason: {reason}");

                if (File.Exists(destPath))
                {
                    // Extremely rare race condition or duplicate, append guid
                    destPath += $"_{Guid.NewGuid()}";
                }

                File.Move(filePath, destPath);
                DebugConsole.WriteWarning($"[QUARANTINE] Moved conflicting file to {destPath}. Reason: {reason}");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"Failed to quarantine file {filePath}: {ex.Message}");
                // If we can't quarantine, we shouldn't proceed with the dangerous operation that triggered this.
                // But for now we just log.
            }
        }
        public List<QuarantinedItem> GetQuarantinedFiles()
        {
            var list = new List<QuarantinedItem>();
            if (!Directory.Exists(_quarantineFolder)) return list;

            var files = Directory.GetFiles(_quarantineFolder);
            foreach (var file in files)
            {
                if (file.EndsWith(".meta.txt")) continue;

                var item = new QuarantinedItem
                {
                    FilePath = file,
                    FileName = Path.GetFileName(file),
                    QuarantinedDate = File.GetCreationTime(file)
                };

                // Try read meta
                string metaPath = file + ".meta.txt";
                if (File.Exists(metaPath))
                {
                    try
                    {
                        var lines = File.ReadAllLines(metaPath);
                        foreach (var line in lines)
                        {
                            if (line.StartsWith("OriginalPath: ")) item.OriginalPath = line.Substring("OriginalPath: ".Length);
                            else if (line.StartsWith("Reason: ")) item.Reason = line.Substring("Reason: ".Length);
                        }
                    }
                    catch { }
                }

                list.Add(item);
            }

            return list.OrderByDescending(x => x.QuarantinedDate).ToList();
        }

        public void RestoreFile(QuarantinedItem item)
        {
            if (!File.Exists(item.FilePath)) throw new FileNotFoundException("Quarantined file not found.", item.FilePath);
            if (string.IsNullOrEmpty(item.OriginalPath)) throw new InvalidOperationException("Original path unknown.");

            // Safety: If original location is blocked, quarantine the BLOCKER first
            if (File.Exists(item.OriginalPath))
            {
                QuarantineFile(item.OriginalPath, "Blocking restoration of backed-up file");
            }

            string? dir = Path.GetDirectoryName(item.OriginalPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            File.Move(item.FilePath, item.OriginalPath);

            // Delete metadata
            string metaPath = item.FilePath + ".meta.txt";
            if (File.Exists(metaPath)) File.Delete(metaPath);

            DebugConsole.WriteSuccess($"Restored {item.FileName} to {item.OriginalPath}");
        }
    }

    public class QuarantinedItem
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string? OriginalPath { get; set; }
        public string? Reason { get; set; }
        public DateTime QuarantinedDate { get; set; }
    }
}
