using System;
using System.IO;
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
    }
}
