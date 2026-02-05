using Newtonsoft.Json.Linq;
using SaveTracker.Resources.HELPERS;
using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;
using static CloudConfig;

namespace SaveTracker.Resources.Logic.RecloneManagement
{
    public class RcloneInstaller
    {
        private readonly RcloneExecutor _executor = new RcloneExecutor();
        private readonly RcloneConfigManager _rcloneConfigManager = new RcloneConfigManager();

        private static string RcloneExePath => RclonePathHelper.RcloneExePath;

        private static readonly string ToolsPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "ExtraTools"
        );

        private readonly string _configPath = Path.Combine(ToolsPath, "rclone.conf");

        private async Task<string> GetLatestRcloneZipUrl()
        {
            DebugConsole.WriteSection("Getting Latest Rclone URL");

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("SaveTracker-Rclone-Updater/1.0");
                client.Timeout = TimeSpan.FromSeconds(30);

                DebugConsole.WriteInfo("Fetching GitHub releases API...");
                var json = await client.GetStringAsync(
                    "https://api.github.com/repos/rclone/rclone/releases/latest"
                );

                JObject root = JObject.Parse(json);

                // Get tag_name
                var version = root["tag_name"]?.ToString();
                DebugConsole.WriteInfo($"Latest version found: {version}");

                // Iterate assets array
                if (root["assets"] is JArray assets)
                {
                    foreach (var asset in assets)
                    {
                        var name = asset["name"]?.ToString();
                        string searchPattern = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                            ? "windows-amd64.zip"
                            : "linux-amd64.zip";

                        if (name != null && name.Contains(searchPattern))
                        {
                            var url = asset["browser_download_url"]?.ToString();
                            DebugConsole.WriteSuccess($"Found download URL: {url}");
                            return url;
                        }
                    }
                }

                DebugConsole.WriteError("No windows-amd64.zip asset found in release");
                return null;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to get latest Rclone URL");
                return null;
            }
        }

        public async Task<bool> RcloneCheckAsync(CloudProvider provider)
        {
            DebugConsole.WriteSection("Rclone Setup Check");

            try
            {
                if (_rcloneConfigManager == null)
                {
                    DebugConsole.WriteError("_rcloneConfigManager is null.");
                    return false;
                }

                DebugConsole.WriteKeyValue("Rclone Path", RcloneExePath);
                DebugConsole.WriteKeyValue("Config Path", _configPath);
                DebugConsole.WriteKeyValue("Tools Directory", ToolsPath);
                DebugConsole.WriteKeyValue("Selected Provider", provider.ToString());

                // Check if rclone.exe exists
                if (string.IsNullOrWhiteSpace(RcloneExePath) || !File.Exists(RcloneExePath))
                {
                    DebugConsole.WriteWarning(
                        "Rclone executable not found, initiating download..."
                    );
                    await DownloadAndInstallRclone();
                }
                else
                {
                    DebugConsole.WriteSuccess("Rclone executable found");
                    var version = await GetRcloneVersion();
                    DebugConsole.WriteInfo($"Rclone version: {version}");
                }

                // Check if config is valid
                string configPath = RclonePathHelper.GetConfigPath(provider);
                if (
                    string.IsNullOrWhiteSpace(configPath)
                    || !File.Exists(configPath)
                    || !await _rcloneConfigManager.IsValidConfig(provider)
                )
                {
                    DebugConsole.WriteWarning("Rclone config invalid or missing, setup required");
                    return false;
                }
                else
                {
                    DebugConsole.WriteSuccess("Rclone configuration is valid");
                    return await _rcloneConfigManager.TestConnection(provider);
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Rclone setup failed");
                return false;
            }
        }

        private async Task DownloadAndInstallRclone()
        {
            DebugConsole.WriteSection("Downloading Rclone");

            try
            {
                DebugConsole.WriteInfo("Starting Rclone download...");

                if (!Directory.Exists(ToolsPath))
                {
                    Directory.CreateDirectory(ToolsPath);
                    DebugConsole.WriteInfo($"Created tools directory: {ToolsPath}");
                }

                var downloadUrl = await GetLatestRcloneZipUrl();
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    throw new Exception("Could not determine Rclone download URL");
                }

                string zipPath = Path.Combine(
                    ToolsPath,
                    $"rclone_{DateTime.Now:yyyyMMdd_HHmmss}.zip"
                );
                DebugConsole.WriteInfo($"Downloading to: {zipPath}");

                using var httpClient = new HttpClient();

                using var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var canReportProgress = totalBytes != -1;

                await using var contentStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[81920];
                long totalRead = 0;
                int read;
                int lastProgress = 0;

                while ((read = await contentStream.ReadAsync(buffer)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read));
                    totalRead += read;

                    if (canReportProgress)
                    {
                        var progress = (int)(totalRead * 100 / totalBytes);
                        if (progress >= lastProgress + 10)
                        {
                            lastProgress = progress;
                            DebugConsole.WriteDebug($"{progress}% ({totalRead:N0}/{totalBytes:N0} bytes)");
                        }
                    }
                }


                DebugConsole.WriteSuccess(
                    $"Download completed: {new FileInfo(zipPath).Length:N0} bytes"
                );

                DebugConsole.WriteInfo("Extracting Rclone...");
                await ExtractRclone(zipPath);

                File.Delete(zipPath);
                DebugConsole.WriteInfo("Cleanup: Deleted temporary zip file");

                DebugConsole.WriteSuccess("Rclone installation completed successfully");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Rclone download/installation failed");
                throw;
            }
        }

        private Task ExtractRclone(string zipPath)
        {
            DebugConsole.WriteInfo("Extracting Rclone archive...");

            try
            {
                string tempExtractPath = Path.Combine(
                    ToolsPath,
                    $"temp_extract_{DateTime.Now:yyyyMMdd_HHmmss}"
                );
                Directory.CreateDirectory(tempExtractPath);

                ZipFile.ExtractToDirectory(zipPath, tempExtractPath);
                DebugConsole.WriteDebug($"Extracted to temporary directory: {tempExtractPath}");

                var extractedFolders = Directory.GetDirectories(tempExtractPath);
                DebugConsole.WriteList(
                    "Extracted folders",
                    extractedFolders.Select(Path.GetFileName)
                );

                string searchPattern = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "windows-amd64"
                    : "linux-amd64";

                var rcloneFolder = extractedFolders.FirstOrDefault(
                    d => d.Contains(searchPattern)
                );
                if (rcloneFolder == null)
                {
                    throw new Exception(
                        $"Could not find rclone folder in extracted files. Found: {string.Join(", ", extractedFolders.Select(Path.GetFileName))}"
                    );
                }

                string rcloneExeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "rclone.exe" : "rclone";
                string rcloneExeSource = Path.Combine(rcloneFolder, rcloneExeName);
                if (!File.Exists(rcloneExeSource))
                {
                    throw new Exception($"{rcloneExeName} not found in {rcloneFolder}");
                }

                File.Copy(rcloneExeSource, RcloneExePath, true);
                DebugConsole.WriteSuccess($"Rclone executable copied to: {RcloneExePath}");

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    try
                    {
                        Process.Start("chmod", $"+x \"{RcloneExePath}\"").WaitForExit();
                        DebugConsole.WriteSuccess("Set executable permissions for rclone");
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteError($"Failed to set permissions: {ex.Message}");
                    }
                }

                Directory.Delete(tempExtractPath, true);
                DebugConsole.WriteDebug("Cleanup: Deleted temporary extraction directory");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Rclone extraction failed");
                throw;
            }

            return Task.CompletedTask;
        }

        private async Task<string> GetRcloneVersion()
        {
            try
            {
                var result = await _executor.ExecuteRcloneCommand(
                    "version --check=false",
                    TimeSpan.FromSeconds(10)
                );
                if (result.Success)
                {
                    var lines = result.Output.Split('\n');
                    var versionLine = lines.FirstOrDefault(l => l.StartsWith("rclone v"));
                    return versionLine?.Trim() ?? "Unknown";
                }
                return "Version check failed";
            }
            catch
            {
                return "Version unavailable";
            }
        }

        /// <summary>
        /// Manually setup config - opens config dialog or wizard
        /// </summary>
        public async Task<bool> SetupConfigAsync(CloudProvider provider)
        {
            DebugConsole.WriteSection($"Setting up Rclone config for {provider}");

            try
            {
                return await _rcloneConfigManager.CreateConfig(provider);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Config setup failed");
                return false;
            }
        }
    }
}
