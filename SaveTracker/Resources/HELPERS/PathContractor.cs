using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SaveTracker.Resources.HELPERS
{
    /// <summary>
    /// Converts absolute file paths to environmental variable paths
    /// </summary>
    public static class PathContractor
    {
        /// <summary>
        /// Contracts a single path to use environment variables
        /// </summary>
        public static string ContractPath(string absolutePath, string gameInstallDirectory)
        {
            if (string.IsNullOrEmpty(absolutePath))
                return absolutePath;

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
                    return $"%GAMEPATH%/{relativePath}";
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
                    return $"{envVar}/{relativePath}";
                }
            }

            // No match found, return original path with forward slashes
            return normalizedPath;
        }

        /// <summary>
        /// Contracts a list of paths to use environment variables
        /// </summary>
        public static List<string> ContractPaths(List<string> absolutePaths, string gameInstallDirectory)
        {
            if (absolutePaths == null)
                return new List<string>();

            var contractedPaths = new List<string>();
            foreach (var path in absolutePaths)
            {
                contractedPaths.Add(ContractPath(path, gameInstallDirectory));
            }
            return contractedPaths;
        }

        /// <summary>
        /// Expands an environmental path back to absolute path
        /// </summary>
        public static string ExpandPath(string contractedPath, string gameInstallDirectory)
        {
            if (string.IsNullOrEmpty(contractedPath))
                return contractedPath;

            string expanded = contractedPath;

            // Handle %GAMEPATH%
            if (!string.IsNullOrEmpty(gameInstallDirectory) &&
                expanded.StartsWith("%GAMEPATH%", StringComparison.OrdinalIgnoreCase))
            {
                expanded = expanded.Replace("%GAMEPATH%/", gameInstallDirectory.Replace('\\', '/') + "/")
                                   .Replace("%GAMEPATH%\\", gameInstallDirectory.Replace('\\', '/') + "/");
            }

            // Expand standard environment variables
            expanded = Environment.ExpandEnvironmentVariables(expanded);

            // Normalize to system path separator
            return expanded.Replace('/', Path.DirectorySeparatorChar);
        }

        /// <summary>
        /// Expands a list of environmental paths back to absolute paths
        /// </summary>
        public static List<string> ExpandPaths(List<string> contractedPaths, string gameInstallDirectory)
        {
            if (contractedPaths == null)
                return new List<string>();

            var expandedPaths = new List<string>();
            foreach (var path in contractedPaths)
            {
                expandedPaths.Add(ExpandPath(path, gameInstallDirectory));
            }
            return expandedPaths;
        }
    }
}
