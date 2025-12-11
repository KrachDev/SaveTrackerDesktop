using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SaveTracker.Resources.HELPERS
{
    /// <summary>
    /// Converts absolute file paths to environmental variable paths
    /// Supports cross-platform Wine prefix path translation
    /// </summary>
    public static class PathContractor
    {
        /// <summary>
        /// Contracts a single path to use environment variables (with optional Wine prefix support)
        /// </summary>
        public static string ContractPath(string absolutePath, string gameInstallDirectory, string? detectedPrefix = null)
        {
            if (string.IsNullOrEmpty(absolutePath))
                return absolutePath;

            // On Linux, try Wine path translation first if we have a detected prefix
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && !string.IsNullOrEmpty(detectedPrefix))
            {
                string winePath = ContractWinePath(absolutePath, detectedPrefix, gameInstallDirectory);
                if (winePath != absolutePath) // If translation succeeded
                    return winePath;
            }

            // Normalize path separators to forward slashes
            string normalizedPath = absolutePath.Replace('\\', '/');

            // Check game directory first (highest priority)
            if (!string.IsNullOrEmpty(gameInstallDirectory))
            {
                string normalizedGameDir = gameInstallDirectory.Replace('\\', '/');
                if (!normalizedGameDir.EndsWith("/"))
                    normalizedGameDir += "/";

                if (normalizedPath.StartsWith(normalizedGameDir, StringComparison.OrdinalIgnoreCase))
                {
                    string relativePath = normalizedPath.Substring(normalizedGameDir.Length);
                    // Use Windows-style backslashes for cloud storage compatibility
                    return $"%GAMEPATH%\\{relativePath.Replace('/', '\\')}";
                }
            }

            // Define environment variable mappings (order matters - check most specific first)
            var envMappings = new List<(string envVar, string path)>
            {
                ("%USERPROFILE%", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
                ("%APPDATA%", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
                ("%LOCALAPPDATA%", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
                ("%PROGRAMDATA%", Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)),
                ("%PUBLIC%", Environment.GetEnvironmentVariable("PUBLIC") ?? ""),
                ("%HOMEDRIVE%", Environment.GetEnvironmentVariable("HOMEDRIVE") ?? ""),
                ("%HOMEPATH%", Environment.GetEnvironmentVariable("HOMEPATH") ?? ""),
                ("%TEMP%", Path.GetTempPath()),
                ("%PROGRAMFILES%", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)),
                ("%PROGRAMFILES(X86)%", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)),
                ("%SYSTEMROOT%", Environment.GetFolderPath(Environment.SpecialFolder.Windows))
            };

            // Try to match against each environment variable
            foreach (var (envVar, envPath) in envMappings)
            {
                if (string.IsNullOrEmpty(envPath))
                    continue;

                string normalizedEnvPath = envPath.Replace('\\', '/');
                if (!normalizedEnvPath.EndsWith("/"))
                    normalizedEnvPath += "/";

                if (normalizedPath.StartsWith(normalizedEnvPath, StringComparison.OrdinalIgnoreCase))
                {
                    string relativePath = normalizedPath.Substring(normalizedEnvPath.Length);
                    // Use Windows-style backslashes for cloud storage compatibility
                    return $"{envVar}\\{relativePath.Replace('/', '\\')}";
                }
            }

            // No match found, return original path with forward slashes
            return normalizedPath;
        }

        /// <summary>
        /// Contracts a Wine prefix path on Linux to Windows environment variable format
        /// Example: /home/user/.wine/drive_c/users/user/Documents/game.sav -> %USERPROFILE%/Documents/game.sav
        /// </summary>
        public static string ContractWinePath(string absoluteLinuxPath, string detectedPrefix, string gameInstallDirectory)
        {
            if (string.IsNullOrEmpty(absoluteLinuxPath) || string.IsNullOrEmpty(detectedPrefix))
                return absoluteLinuxPath;

            string normalizedPath = absoluteLinuxPath.Replace('\\', '/');
            string normalizedPrefix = detectedPrefix.Replace('\\', '/');
            if (!normalizedPrefix.EndsWith("/"))
                normalizedPrefix += "/";

            // Check if path is inside the Wine prefix
            if (!normalizedPath.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
                return absoluteLinuxPath; // Not a Wine path

            // Check if it's inside drive_c
            string driveC = Path.Combine(normalizedPrefix, "drive_c").Replace('\\', '/') + "/";
            if (!normalizedPath.StartsWith(driveC, StringComparison.OrdinalIgnoreCase))
                return absoluteLinuxPath; // Not in drive_c

            // Get the path relative to drive_c
            string relativeToDriveC = normalizedPath.Substring(driveC.Length);

            // Wine path mappings (case-insensitive for flexibility)
            // Pattern: users/<username>/Documents -> %USERPROFILE%/Documents
            var wineMappings = new List<(string winePattern, string envVar)>
            {
                ("users/", "%USERPROFILE%"), // Generic user folder mapping
                ("programdata/", "%PROGRAMDATA%")
            };

            foreach (var (winePattern, envVar) in wineMappings)
            {
                if (relativeToDriveC.StartsWith(winePattern, StringComparison.OrdinalIgnoreCase))
                {
                    // For users/<username>/, we need to skip the username part
                    if (winePattern == "users/")
                    {
                        // Find the next slash after "users/"
                        int userStart = winePattern.Length;
                        int nextSlash = relativeToDriveC.IndexOf('/', userStart);

                        if (nextSlash > userStart)
                        {
                            // Check for AppData subdirectories
                            string afterUsername = relativeToDriveC.Substring(nextSlash + 1);

                            // More specific AppData mappings
                            if (afterUsername.StartsWith("AppData/Roaming/", StringComparison.OrdinalIgnoreCase))
                            {
                                string afterAppData = afterUsername.Substring("AppData/Roaming/".Length);
                                // Use Windows-style backslashes for cloud storage compatibility
                                return $"%APPDATA%\\{afterAppData.Replace('/', '\\')}";
                            }
                            else if (afterUsername.StartsWith("AppData/Local/", StringComparison.OrdinalIgnoreCase))
                            {
                                string afterAppData = afterUsername.Substring("AppData/Local/".Length);
                                // Use Windows-style backslashes for cloud storage compatibility
                                return $"%LOCALAPPDATA%\\{afterAppData.Replace('/', '\\')}";
                            }
                            else
                            {
                                // Regular user profile path (Documents, etc.)
                                // Use Windows-style backslashes for cloud storage compatibility
                                return $"{envVar}\\{afterUsername.Replace('/', '\\')}";
                            }
                        }
                    }
                    else
                    {
                        // For other patterns, just replace the prefix
                        string afterPattern = relativeToDriveC.Substring(winePattern.Length);
                        // Use Windows-style backslashes for cloud storage compatibility
                        return $"{envVar}\\{afterPattern.Replace('/', '\\')}";
                    }
                }
            }

            // Fallback: if it's in drive_c but we couldn't map it to an env var,
            // use a generic C: notation with Windows-style backslashes
            return $"C:\\{relativeToDriveC.Replace('/', '\\')}";
        }

        /// <summary>
        /// Expands an environmental path back to absolute path (with optional Wine prefix support)
        /// </summary>
        public static string ExpandPath(string contractedPath, string gameInstallDirectory, string? detectedPrefix = null)
        {
            if (string.IsNullOrEmpty(contractedPath))
                return contractedPath;

            // On Linux, try Wine path expansion if we have a detected prefix
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && !string.IsNullOrEmpty(detectedPrefix))
            {
                string expanded = ExpandWinePath(contractedPath, detectedPrefix, gameInstallDirectory);
                if (expanded != contractedPath) // If expansion succeeded
                    return expanded;
            }

            string result = contractedPath;

            // Handle %GAMEPATH%
            if (!string.IsNullOrEmpty(gameInstallDirectory) &&
                result.StartsWith("%GAMEPATH%", StringComparison.OrdinalIgnoreCase))
            {
                result = result.Replace("%GAMEPATH%/", gameInstallDirectory.Replace('\\', '/') + "/")
                               .Replace("%GAMEPATH%\\", gameInstallDirectory.Replace('\\', '/') + "/");
            }

            // Expand standard environment variables
            result = Environment.ExpandEnvironmentVariables(result);

            // Normalize to system path separator
            return result.Replace('/', Path.DirectorySeparatorChar);
        }

        /// <summary>
        /// Expands a Windows environment variable path to Linux Wine prefix path
        /// Example: %USERPROFILE%/Documents/game.sav -> /home/user/.wine/drive_c/users/user/Documents/game.sav
        /// </summary>
        public static string ExpandWinePath(string contractedPath, string detectedPrefix, string gameInstallDirectory)
        {
            if (string.IsNullOrEmpty(contractedPath) || string.IsNullOrEmpty(detectedPrefix))
                return contractedPath;

            string normalizedPrefix = detectedPrefix.Replace('\\', '/');
            if (!normalizedPrefix.EndsWith("/"))
                normalizedPrefix += "/";

            string driveC = normalizedPrefix + "drive_c/";

            // Check for Windows environment variables
            if (contractedPath.StartsWith("%USERPROFILE%", StringComparison.OrdinalIgnoreCase))
            {
                // Find the Wine username by looking for users/* directory
                string usersDir = Path.Combine(driveC, "users");
                if (Directory.Exists(usersDir))
                {
                    try
                    {
                        var userDirs = Directory.GetDirectories(usersDir);
                        if (userDirs.Length > 0)
                        {
                            // Use the first user directory (typically there's only one, like "steamuser")
                            string username = Path.GetFileName(userDirs[0]);
                            string afterEnvVar = contractedPath.Substring("%USERPROFILE%".Length).TrimStart('/', '\\');
                            return Path.Combine(driveC, "users", username, afterEnvVar).Replace('\\', '/');
                        }
                    }
                    catch { }
                }

                // Fallback: assume "steamuser" (common for Proton)
                string afterVar = contractedPath.Substring("%USERPROFILE%".Length).TrimStart('/', '\\');
                return Path.Combine(driveC, "users", "steamuser", afterVar).Replace('\\', '/');
            }
            else if (contractedPath.StartsWith("%APPDATA%", StringComparison.OrdinalIgnoreCase))
            {
                string afterVar = contractedPath.Substring("%APPDATA%".Length).TrimStart('/', '\\');
                return ExpandWinePath($"%USERPROFILE%/AppData/Roaming/{afterVar}", detectedPrefix, gameInstallDirectory);
            }
            else if (contractedPath.StartsWith("%LOCALAPPDATA%", StringComparison.OrdinalIgnoreCase))
            {
                string afterVar = contractedPath.Substring("%LOCALAPPDATA%".Length).TrimStart('/', '\\');
                return ExpandWinePath($"%USERPROFILE%/AppData/Local/{afterVar}", detectedPrefix, gameInstallDirectory);
            }
            else if (contractedPath.StartsWith("%PROGRAMDATA%", StringComparison.OrdinalIgnoreCase))
            {
                string afterVar = contractedPath.Substring("%PROGRAMDATA%".Length).TrimStart('/', '\\');
                return Path.Combine(driveC, "ProgramData", afterVar).Replace('\\', '/');
            }
            else if (contractedPath.StartsWith("C:/", StringComparison.OrdinalIgnoreCase) ||
                     contractedPath.StartsWith("C:\\", StringComparison.OrdinalIgnoreCase))
            {
                // Generic C: drive mapping
                string afterC = contractedPath.Substring(2).TrimStart('/', '\\');
                return Path.Combine(driveC, afterC).Replace('\\', '/');
            }

            return contractedPath;
        }

        /// <summary>
        /// Contracts a list of paths to use environment variables
        /// </summary>
        public static List<string> ContractPaths(List<string> absolutePaths, string gameInstallDirectory, string? detectedPrefix = null)
        {
            if (absolutePaths == null)
                return new List<string>();

            var contractedPaths = new List<string>();
            foreach (var path in absolutePaths)
            {
                contractedPaths.Add(ContractPath(path, gameInstallDirectory, detectedPrefix));
            }
            return contractedPaths;
        }

        /// <summary>
        /// Expands a list of environmental paths back to absolute paths
        /// </summary>
        public static List<string> ExpandPaths(List<string> contractedPaths, string gameInstallDirectory, string? detectedPrefix = null)
        {
            if (contractedPaths == null)
                return new List<string>();

            var expandedPaths = new List<string>();
            foreach (var path in contractedPaths)
            {
                expandedPaths.Add(ExpandPath(path, gameInstallDirectory, detectedPrefix));
            }
            return expandedPaths;
        }
    }
}
