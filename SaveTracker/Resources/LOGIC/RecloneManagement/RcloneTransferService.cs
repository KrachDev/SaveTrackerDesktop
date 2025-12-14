using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SaveTracker.Resources.HELPERS;
using static CloudConfig;

using System.Text.RegularExpressions;
using SaveTracker.Resources.Logic.RecloneManagement;

namespace SaveTracker.Resources.Logic.RecloneManagement
{
    public class RcloneTransferService
    {
        // Relaxed regex to match "12%" or " 12%" or ", 12%"
        private static readonly Regex ProgressRegex = new Regex(@"(\d+)%", RegexOptions.Compiled);
        private readonly RcloneExecutor _executor = new RcloneExecutor();
        private readonly int _maxRetries = 3;
        private readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(2);
        private readonly TimeSpan _processTimeout = TimeSpan.FromMinutes(10);

        public async Task<bool> UploadFileWithRetry(
            string localPath,
            string remotePath,
            string fileName,
            CloudProvider provider,
            IProgress<double>? progress = null)
        {
            for (int attempt = 1; attempt <= _maxRetries; attempt++)
            {
                try
                {
                    DebugConsole.WriteDebug(
                        $"Upload attempt {attempt}/{_maxRetries} for {fileName}"
                    );

                    string configPath = RclonePathHelper.GetConfigPath(provider);
                    var result = await _executor.ExecuteRcloneCommand(
                        $"copyto \"{localPath}\" \"{remotePath}\" --config \"{configPath}\" --progress",
                        _processTimeout,
                        hideWindow: true,
                        allowedExitCodes: null,
                        onOutput: (line) =>
                        {
                            var match = ProgressRegex.Match(line);
                            if (match.Success && double.TryParse(match.Groups[1].Value, out double percent))
                            {
                                progress?.Report(percent);
                            }
                        }
                    );

                    if (result.Success)
                    {
                        DebugConsole.WriteSuccess($"Upload successful on attempt {attempt}");
                        return true;
                    }
                    else
                    {
                        DebugConsole.WriteWarning($"Attempt {attempt} failed: {result.Error}");

                        if (attempt < _maxRetries)
                        {
                            DebugConsole.WriteInfo(
                                $"Waiting {_retryDelay.TotalSeconds} seconds before retry..."
                            );
                            await Task.Delay(_retryDelay);
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteException(ex, $"Upload attempt {attempt} exception");

                    if (attempt < _maxRetries)
                    {
                        await Task.Delay(_retryDelay);
                    }
                }
            }

            return false;
        }

        public async Task<bool> UploadBatchAsync(
            string sourceRoot,
            string remoteRoot,
            List<string> relativeFilePaths,
            CloudProvider provider,
            IProgress<double>? progress = null)
        {
            // Create a temporary file list for rclone
            string listFilePath = System.IO.Path.GetTempFileName();

            try
            {
                // Write relative paths to the list file
                // Rclone expects forward slashes for paths in the list file
                var formattedPaths = new List<string>();
                foreach (var path in relativeFilePaths)
                {
                    formattedPaths.Add(path.Replace('\\', '/'));
                }

                await System.IO.File.WriteAllLinesAsync(listFilePath, formattedPaths);

                for (int attempt = 1; attempt <= _maxRetries; attempt++)
                {
                    try
                    {
                        DebugConsole.WriteDebug($"Batch upload attempt {attempt}/{_maxRetries} for {relativeFilePaths.Count} files");

                        string configPath = RclonePathHelper.GetConfigPath(provider);

                        // Use 'copy' instead of 'copyto' when using --files-from
                        // The source is the root directory
                        // The destination is the remote root directory
                        // --transfers=8 and --checkers=16 for maximum parallelism
                        var result = await _executor.ExecuteRcloneCommand(
                            $"copy \"{sourceRoot}\" \"{remoteRoot}\" --files-from \"{listFilePath}\" --config \"{configPath}\" --transfers=8 --checkers=16 --progress",
                            _processTimeout,
                            hideWindow: true,
                            allowedExitCodes: null,
                            onOutput: (line) =>
                            {
                                var match = ProgressRegex.Match(line);
                                if (match.Success && double.TryParse(match.Groups[1].Value, out double percent))
                                {
                                    progress?.Report(percent);
                                }
                            }
                        );

                        if (result.Success)
                        {
                            DebugConsole.WriteSuccess($"Batch upload successful on attempt {attempt}");
                            return true;
                        }
                        else
                        {
                            DebugConsole.WriteWarning($"Batch attempt {attempt} failed: {result.Error}");

                            if (attempt < _maxRetries)
                            {
                                DebugConsole.WriteInfo($"Waiting {_retryDelay.TotalSeconds} seconds before retry...");
                                await Task.Delay(_retryDelay);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteException(ex, $"Batch upload attempt {attempt} exception");

                        if (attempt < _maxRetries)
                        {
                            await Task.Delay(_retryDelay);
                        }
                    }
                }
            }
            finally
            {
                // Clean up the temporary file
                try
                {
                    if (System.IO.File.Exists(listFilePath))
                    {
                        System.IO.File.Delete(listFilePath);
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteWarning($"Failed to delete temp file {listFilePath}: {ex.Message}");
                }
            }

            return false;
        }

        public async Task<bool> DownloadFileWithRetry(
            string remotePath,
            string localPath,
            string fileName,
            CloudProvider provider,
            IProgress<double>? progress = null)
        {
            for (int attempt = 1; attempt <= _maxRetries; attempt++)
            {
                try
                {
                    DebugConsole.WriteDebug(
                        $"Download attempt {attempt}/{_maxRetries} for {fileName}"
                    );

                    string configPath = RclonePathHelper.GetConfigPath(provider);
                    var result = await _executor.ExecuteRcloneCommand(
                        $"copyto \"{remotePath}\" \"{localPath}\" --config \"{configPath}\" --progress",
                        _processTimeout,
                        hideWindow: true,
                        allowedExitCodes: null,
                        onOutput: (line) =>
                        {
                            var match = ProgressRegex.Match(line);
                            if (match.Success && double.TryParse(match.Groups[1].Value, out double percent))
                            {
                                progress?.Report(percent);
                            }
                        }
                    );

                    if (result.Success)
                    {
                        DebugConsole.WriteSuccess($"Download successful on attempt {attempt}");
                        return true;
                    }
                    else
                    {
                        DebugConsole.WriteWarning($"Attempt {attempt} failed: {result.Error}");

                        if (attempt < _maxRetries)
                        {
                            DebugConsole.WriteInfo(
                                $"Waiting {_retryDelay.TotalSeconds} seconds before retry..."
                            );
                            await Task.Delay(_retryDelay);
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteException(ex, $"Download attempt {attempt} exception");

                    if (attempt < _maxRetries)
                    {
                        await Task.Delay(_retryDelay);
                    }
                }
            }

            return false;
        }

        public async Task<bool> DownloadDirectory(string remotePath, string localPath, CloudProvider provider, IProgress<double>? progress = null)
        {
            try
            {
                DebugConsole.WriteInfo($"Downloading directory from {remotePath} to {localPath}");

                string configPath = RclonePathHelper.GetConfigPath(provider);
                var result = await _executor.ExecuteRcloneCommand(
                    $"copy \"{remotePath}\" \"{localPath}\" --config \"{configPath}\" --transfers=8 --checkers=16 --progress",
                    TimeSpan.FromMinutes(5),
                    hideWindow: true,
                    allowedExitCodes: null,
                    onOutput: (line) =>
                    {
                        var match = ProgressRegex.Match(line);
                        if (match.Success && double.TryParse(match.Groups[1].Value, out double percent))
                        {
                            progress?.Report(percent);
                        }
                    }
                );

                if (result.Success)
                {
                    DebugConsole.WriteSuccess("Directory download successful");
                    return true;
                }
                else
                {
                    DebugConsole.WriteError($"Directory download failed: {result.Error}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Directory download exception");
                return false;
            }
        }

        public async Task<bool> RemoteFileExists(string remotePath, CloudProvider provider)
        {
            try
            {
                string configPath = RclonePathHelper.GetConfigPath(provider);
                var result = await _executor.ExecuteRcloneCommand(
                    $"lsl \"{remotePath}\" --config \"{configPath}\"",
                    TimeSpan.FromSeconds(15),
                    hideWindow: true,
                    allowedExitCodes: new[] { 3 }
                );
                return result.Success && !string.IsNullOrWhiteSpace(result.Output);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to check remote file existence");
                return false;
            }
        }

        public async Task<bool> CheckCloudSaveExistsAsync(string remoteBasePath, CloudProvider provider)
        {
            try
            {
                string configPath = RclonePathHelper.GetConfigPath(provider);
                var result = await _executor.ExecuteRcloneCommand(
                    $"lsf \"{remoteBasePath}\" --config \"{configPath}\" --max-depth 1",
                    TimeSpan.FromSeconds(15),
                    hideWindow: true,
                    allowedExitCodes: new[] { 3 }
                );

                return result.Success && !string.IsNullOrWhiteSpace(result.Output);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to check cloud save existence");
                return false;
            }
        }

        public async Task<bool> RenameFolder(string oldRemotePath, string newRemotePath, CloudProvider provider)
        {
            try
            {
                DebugConsole.WriteInfo($"Renaming cloud folder from {oldRemotePath} to {newRemotePath}");

                string configPath = RclonePathHelper.GetConfigPath(provider);

                // rclone moveto source dest
                var result = await _executor.ExecuteRcloneCommand(
                    $"moveto \"{oldRemotePath}\" \"{newRemotePath}\" --config \"{configPath}\"",
                    TimeSpan.FromMinutes(2) // Give it some time if it needs to move a lot (though moveto is usually fast for rename)
                );

                if (result.Success)
                {
                    DebugConsole.WriteSuccess("Cloud folder rename successful");
                    return true;
                }
                else
                {
                    DebugConsole.WriteError($"Cloud folder rename failed: {result.Error}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Cloud folder rename exception");
                return false;
            }
        }

        /// <summary>
        /// List all files in a cloud directory (recursive)
        /// </summary>
        public async Task<List<string>> ListCloudFiles(string remotePath, CloudProvider provider)
        {
            try
            {
                string configPath = RclonePathHelper.GetConfigPath(provider);
                var result = await _executor.ExecuteRcloneCommand(
                    $"lsf \"{remotePath}\" --recursive --config \"{configPath}\"",
                    TimeSpan.FromSeconds(30),
                    hideWindow: true
                );

                if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
                {
                    // Split by newlines and filter out empty entries
                    var files = result.Output
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(f => f.Trim())
                        .Where(f => !string.IsNullOrWhiteSpace(f))
                        .ToList();

                    return files;
                }

                return new List<string>();
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to list cloud files");
                return new List<string>();
            }
        }
        public async Task<List<string>> ListCloudDirectories(string remotePath, CloudProvider provider)
        {
            try
            {
                string configPath = RclonePathHelper.GetConfigPath(provider);
                // "lsd" lists only directories
                var result = await _executor.ExecuteRcloneCommand(
                    $"lsd \"{remotePath}\" --config \"{configPath}\"",
                    TimeSpan.FromSeconds(30),
                    hideWindow: true
                );

                if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
                {
                    var dirs = new List<string>();
                    var lines = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        // Output format: "          -1 2023-10-27 12:00:00        -1 FolderName"
                        // lsd output usually ends with the folder name
                        // We need to parse the last part
                        var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0)
                        {
                            // The folder name is the last part, but it might contain spaces, so we need to be careful with Split.
                            // simpler approach: look for the last index of "-1 " or similar, or just take substring.
                            // Standard lsd output: <size> <date> <time> <count> <name>
                            // Example:           -1 2025-12-14 14:00:00        -1 Game Name
                            // The first 4 columns are: size(-1), date, time, count(-1)

                            // Let's try to match 4 columns then the rest is name
                            if (parts.Length >= 5)
                            {
                                // Reconstruct name from index 4 onwards
                                string name = string.Join(" ", parts.Skip(4));
                                dirs.Add(name);
                            }
                        }
                    }
                    return dirs;
                }

                return new List<string>();
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to list cloud directories");
                return new List<string>();
            }
        }
    }
}

