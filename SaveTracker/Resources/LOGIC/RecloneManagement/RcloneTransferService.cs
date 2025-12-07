using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SaveTracker.Resources.HELPERS;
using static CloudConfig;

namespace SaveTracker.Resources.Logic.RecloneManagement
{
    public class RcloneTransferService
    {
        private readonly RcloneExecutor _executor = new RcloneExecutor();
        private readonly int _maxRetries = 3;
        private readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(2);
        private readonly TimeSpan _processTimeout = TimeSpan.FromMinutes(10);

        public async Task<bool> UploadFileWithRetry(
            string localPath,
            string remotePath,
            string fileName,
            CloudProvider provider)
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
                        _processTimeout
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

        public async Task<bool> DownloadFileWithRetry(
            string remotePath,
            string localPath,
            string fileName,
            CloudProvider provider)
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
                        _processTimeout
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

        public async Task<bool> DownloadDirectory(string remotePath, string localPath, CloudProvider provider)
        {
            try
            {
                DebugConsole.WriteInfo($"Downloading directory from {remotePath} to {localPath}");

                string configPath = RclonePathHelper.GetConfigPath(provider);
                var result = await _executor.ExecuteRcloneCommand(
                    $"copy \"{remotePath}\" \"{localPath}\" --config \"{configPath}\" --progress",
                    TimeSpan.FromMinutes(5)
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
                    TimeSpan.FromSeconds(15)
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
                    TimeSpan.FromSeconds(15)
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
    }
}
