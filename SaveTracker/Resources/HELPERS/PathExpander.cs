using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SaveTracker.Resources.HELPERS
{
    public static class PathExpander
    {
        // Dictionary of known path variables and their actual paths
        private static readonly Dictionary<string, string> _pathVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "%USERPROFILE%", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) },
            { "%USER%", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) },
            { "%APPDATA%", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) },
            { "%LOCALAPPDATA%", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) },
            { "%DOCUMENTS%", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) },
            { "%PROGRAMFILES%", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) },
            { "%PROGRAMFILESX86%", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) },
            { "%TEMP%", Path.GetTempPath().TrimEnd('\\', '/') },
            { "%SYSTEMDRIVE%", Path.GetPathRoot(Environment.SystemDirectory).TrimEnd('\\', '/') },
        };

        // Can be set dynamically for game-specific paths
        private static string _gamePath = null;

        public static void SetGamePath(string gamePath)
        {
            _gamePath = gamePath?.TrimEnd('\\', '/');
        }

        /// <summary>
        /// Case-insensitive string replacement helper for older .NET versions
        /// </summary>
        private static string ReplaceIgnoreCase(string input, string oldValue, string newValue)
        {
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(oldValue))
                return input;

            int index = input.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);

            while (index >= 0)
            {
                input = input.Substring(0, index) + newValue + input.Substring(index + oldValue.Length);
                index = input.IndexOf(oldValue, index + newValue.Length, StringComparison.OrdinalIgnoreCase);
            }

            return input;
        }

        /// <summary>
        /// Expands a path with variables to an absolute path
        /// Example: "%USER%/AppData/file.txt" -> "C:/Users/PERSON/AppData/file.txt"
        /// </summary>
        public static string ExpandPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            string expandedPath = path;

            // Expand %GAMEPATH% first if set
            if (!string.IsNullOrEmpty(_gamePath) &&
                expandedPath.IndexOf("%GAMEPATH%", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                expandedPath = ReplaceIgnoreCase(expandedPath, "%GAMEPATH%", _gamePath);
            }

            // Expand other variables
            foreach (var variable in _pathVariables)
            {
                if (expandedPath.IndexOf(variable.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    expandedPath = ReplaceIgnoreCase(expandedPath, variable.Key, variable.Value);
                }
            }

            // Normalize path separators
            expandedPath = expandedPath.Replace('\\', '/');

            return expandedPath;
        }

        /// <summary>
        /// Contracts an absolute path to use variables
        /// Example: "C:/Users/PERSON/AppData/file.txt" -> "%USER%/AppData/file.txt"
        /// </summary>
        public static string ContractPath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
                return absolutePath;

            string contractedPath = absolutePath.Replace('\\', '/');

            // Try to contract with %GAMEPATH% first (longest/most specific match)
            if (!string.IsNullOrEmpty(_gamePath))
            {
                string normalizedGamePath = _gamePath.Replace('\\', '/');
                if (contractedPath.StartsWith(normalizedGamePath, StringComparison.OrdinalIgnoreCase))
                {
                    contractedPath = "%GAMEPATH%" + contractedPath.Substring(normalizedGamePath.Length);
                    return contractedPath;
                }
            }

            // Sort variables by path length (longest first) for best match
            var sortedVariables = _pathVariables
                .OrderByDescending(kvp => kvp.Value.Length)
                .ToList();

            foreach (var variable in sortedVariables)
            {
                string normalizedVarPath = variable.Value.Replace('\\', '/');

                if (contractedPath.StartsWith(normalizedVarPath, StringComparison.OrdinalIgnoreCase))
                {
                    contractedPath = variable.Key + contractedPath.Substring(normalizedVarPath.Length);
                    break;
                }
            }

            return contractedPath;
        }

        /// <summary>
        /// Contracts a list of absolute paths to use variables
        /// </summary>
        public static List<string> ContractPaths(IEnumerable<string> absolutePaths)
        {
            return absolutePaths.Select(ContractPath).ToList();
        }

        /// <summary>
        /// Expands a list of paths with variables to absolute paths
        /// </summary>
        public static List<string> ExpandPaths(IEnumerable<string> paths)
        {
            return paths.Select(ExpandPath).ToList();
        }
    }

}
