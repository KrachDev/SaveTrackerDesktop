using SaveTracker.Resources.HELPERS;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace SaveTracker.Resources.Logic.AutoUpdater
{
    /// <summary>
    /// Service to download update files from GitHub
    /// </summary>
    public class UpdateDownloader
    {
        private static readonly string UpdateCachePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "UpdateCache"
        );

        /// <summary>
        /// Event fired when download progress changes (percentage)
        /// </summary>
        public event EventHandler<int>? DownloadProgressChanged;

        /// <summary>
        /// Downloads the update executable to the cache folder
        /// </summary>
        /// <param name="downloadUrl">URL to download from</param>
        /// <returns>Path to the downloaded file</returns>
        public async Task<string> DownloadUpdateAsync(string downloadUrl)
        {
            DebugConsole.WriteSection("Downloading Update");
            DebugConsole.WriteKeyValue("Download URL", downloadUrl);

            try
            {
                // Create cache directory if it doesn't exist
                if (!Directory.Exists(UpdateCachePath))
                {
                    Directory.CreateDirectory(UpdateCachePath);
                    DebugConsole.WriteInfo($"Created cache directory: {UpdateCachePath}");
                }

                // Clean up old files in cache
                CleanupCache();

                // Determine file path
                string fileName = "SaveTracker_Update.exe";
                string filePath = Path.Combine(UpdateCachePath, fileName);

                DebugConsole.WriteInfo($"Downloading to: {filePath}");

                // Download the file using HttpClient
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(10); // Allow up to 10 minutes for download

                    using (var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();

                        var totalBytes = response.Content.Headers.ContentLength ?? 0;
                        var downloadedBytes = 0L;

                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
                            var buffer = new byte[8192];
                            int bytesRead;
                            var lastReportedProgress = 0;

                            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead);
                                downloadedBytes += bytesRead;

                                // Calculate progress percentage
                                var progressPercentage = totalBytes > 0 ? (int)((downloadedBytes * 100) / totalBytes) : 0;

                                // Report progress every 10%
                                if (progressPercentage >= lastReportedProgress + 10)
                                {
                                    lastReportedProgress = progressPercentage;
                                    DebugConsole.WriteDebug($"Download progress: {progressPercentage}% ({downloadedBytes:N0}/{totalBytes:N0} bytes)");

                                    // Fire event for UI updates
                                    DownloadProgressChanged?.Invoke(this, progressPercentage);
                                }
                            }
                        }
                    }
                }

                // Verify the file was downloaded
                if (!File.Exists(filePath))
                {
                    throw new Exception("Download completed but file not found");
                }

                var fileInfo = new FileInfo(filePath);
                DebugConsole.WriteSuccess($"Download completed: {fileInfo.Length:N0} bytes");

                // Basic validation - file should be at least 1MB
                if (fileInfo.Length < 1_000_000)
                {
                    throw new Exception($"Downloaded file is too small ({fileInfo.Length} bytes), may be corrupted");
                }

                return filePath;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to download update");
                throw;
            }
        }

        /// <summary>
        /// Cleans up old files in the cache directory
        /// </summary>
        private void CleanupCache()
        {
            try
            {
                if (Directory.Exists(UpdateCachePath))
                {
                    var files = Directory.GetFiles(UpdateCachePath);
                    foreach (var file in files)
                    {
                        try
                        {
                            File.Delete(file);
                            DebugConsole.WriteDebug($"Deleted old cache file: {Path.GetFileName(file)}");
                        }
                        catch (Exception ex)
                        {
                            DebugConsole.WriteWarning($"Failed to delete cache file {Path.GetFileName(file)}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Failed to cleanup cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the cache directory path
        /// </summary>
        public static string GetCachePath() => UpdateCachePath;
    }
}
