using Ico.Reader;
using SaveTracker.Resources.Logic;
using SaveTracker.Resources.Logic.RecloneManagement;
using SaveTracker.Resources.SAVE_SYSTEM;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SaveTracker.Resources.HELPERS
{
    public class Misc
    {
        public static string GetExecutableDescription(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return string.Empty;

            try
            {
                var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);

                // First, try embedded metadata
                if (IsValidName(info.FileDescription))
                    return info.FileDescription!;
                if (IsValidName(info.ProductName))
                    return info.ProductName!;
            }
            catch
            {
                // Ignore errors (e.g. native Linux ELF files, permissions)
            }

            // Fallback for Linux/Non-Windows where FileVersionInfo might return nulls or fail for PE files
            try
            {
                if (PeNet.PeFile.TryParse(path, out var peFile))
                {
                    if (peFile?.Resources?.VsVersionInfo?.StringFileInfo?.StringTable != null)
                    {
                        foreach (var table in peFile.Resources.VsVersionInfo.StringFileInfo.StringTable)
                        {
                            if (IsValidName(table.ProductName)) return table.ProductName!;
                            if (IsValidName(table.FileDescription)) return table.FileDescription!;
                        }
                    }
                }
            }
            catch { }

            // Fallback: filename or parent folder
            string name = Path.GetFileNameWithoutExtension(path);
            var parentDir = Path.GetDirectoryName(path);
            var parentDirName = Path.GetFileName(parentDir);

            // If filename is generic or too short, and we have a parent directory name, use that.
            // Generic names: game, launch, start, setup, app, client
            var lowerName = name.ToLowerInvariant();
            var genericNames = new HashSet<string> { "game", "launch", "launcher", "start", "setup", "app", "client", "main", "play" };

            if (!string.IsNullOrWhiteSpace(parentDirName) &&
                (name.Length <= 3 || genericNames.Contains(lowerName) || lowerName.StartsWith("re2"))) // Specific fix for this user's case + generic
            {
                name = parentDirName;
            }
            else
            {
                // Optional: insert spaces for CamelCase / PascalCase ONLY if we kept the filename
                name = System.Text.RegularExpressions.Regex.Replace(name, "(\\B[A-Z])", " $1");
            }

            return name;
        }

        private static bool IsValidName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            // Filter out garbage characters often found in Linux read of PE headers
            // The user saw "" which is \uFFFD replacement char
            if (name.Contains('\uFFFD') || name.All(c => !char.IsLetterOrDigit(c))) return false;
            return true;
        }

        public static byte[]? ExtractIconFromExe(string exePath)
        {
             return ExtractIconDataFromExe(exePath);
        }

        public static byte[]? ExtractIconDataFromExe(string exePath)
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                return null;

            try
            {
                return ExtractIconDataCrossPlatform(exePath);
            }
            catch
            {
            }
            return null;
        }

        private static byte[]? ExtractIconDataCrossPlatform(string exePath)
        {
            try
            {
                var icoReader = new IcoReader();
                var icoData = icoReader.Read(exePath);

                if (icoData == null || icoData.Groups == null || icoData.Groups.Count == 0)
                    return null;

                var group = icoData.Groups[0];
                if (group.DirectoryEntries == null || group.DirectoryEntries.Length == 0)
                    return null;

                int largestIndex = 0;
                int maxSize = 0;

                for (int i = 0; i < group.DirectoryEntries.Length; i++)
                {
                    var entry = group.DirectoryEntries[i];
                    int size = entry.Width * entry.Height;
                    if (size > maxSize)
                    {
                        maxSize = size;
                        largestIndex = i;
                    }
                }

                return icoData.GetImage(group, largestIndex);
            }
            catch (Exception)
            {
                return null;
            }
        }


        public static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
        public static (string text, string color) GetStatusInfo(FileChecksumRecord file, string lastSyncStatus)
        {
            if (string.IsNullOrEmpty(file.Checksum))
                return ("Pending", "#F59E0B"); // Orange

            if (lastSyncStatus == "Failed")
                return ("Failed", "#EF4444"); // Red

            return ("Synced", "#10B981"); // Green
        }
        public static bool HasDoubleExtension(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            string fileName = Path.GetFileName(filePath);
            if (fileName == null)
                return false;

            // Count the dots in the filename
            int dotCount = fileName.Split('.').Length - 1;

            return dotCount >= 2;
        }
        public static string RemoveLastExtension(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return filePath;

            string directory = Path.GetDirectoryName(filePath) ?? "";
            string fileName = Path.GetFileName(filePath);

            int lastDot = fileName.LastIndexOf('.');
            if (lastDot <= 0) // no extension or hidden files like ".gitignore"
                return filePath;

            string newFileName = fileName.Substring(0, lastDot);
            return Path.Combine(directory, newFileName);
        }

        public static bool ShouldWeCheckForSaveExists(Game game)
        {
            var checksumService = new ChecksumService();
            string saveJson = checksumService.GetChecksumFilePath(game.InstallDirectory, game.ActiveProfileId);

            if (!File.Exists(saveJson))
                return true;

            return false;
        }
    }

}
