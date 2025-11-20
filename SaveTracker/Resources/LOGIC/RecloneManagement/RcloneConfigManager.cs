using Newtonsoft.Json;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.LOGIC.RecloneManagement;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static CloudConfig;

namespace SaveTracker.Resources.LOGIC.RecloneManagement
{
    public class RcloneConfigManager
    {
        private readonly CloudProviderHelper _cloudProviderHelper = new CloudProviderHelper();
        private readonly RcloneExecutor _rcloneExecutor = new RcloneExecutor();

        public static string RcloneExePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtraTools", "rclone.exe");

        private static readonly string ToolsPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "ExtraTools"
        );

        private readonly string _configPath = Path.Combine(ToolsPath, "rclone.conf");

        /// <summary>
        /// Validates if the rclone config file is valid for the specified provider
        /// </summary>
        public Task<bool> IsValidConfig(string path, CloudProvider provider)
        {
            string providerName = _cloudProviderHelper.GetProviderConfigName(provider);
            string providerType = _cloudProviderHelper.GetProviderConfigType(provider);
            string displayName = _cloudProviderHelper.GetProviderDisplayName(provider);

            DebugConsole.WriteInfo($"Validating {displayName} config file: {path}");

            try
            {
                if (!File.Exists(path))
                {
                    DebugConsole.WriteWarning("Config file does not exist");
                    return Task.FromResult(false);
                }

                string content = File.ReadAllText(path);
                DebugConsole.WriteDebug($"Config file size: {content.Length} characters");

                // Check for provider-specific section
                string sectionName = $"[{providerName}]";
                if (!content.Contains(sectionName))
                {
                    DebugConsole.WriteWarning($"Config missing {sectionName} section");
                    return Task.FromResult(false);
                }

                // Check for provider-specific type
                string typeString = $"type = {providerType}";
                if (!content.Contains(typeString))
                {
                    DebugConsole.WriteWarning($"Config missing '{typeString}' setting");
                    return Task.FromResult(false);
                }

                // Token validation (if required by provider)
                if (_cloudProviderHelper.RequiresTokenValidation(provider))
                {
                    var tokenMatch = Regex.Match(content, @"token\s*=\s*(.+)");
                    if (!tokenMatch.Success)
                    {
                        DebugConsole.WriteWarning("Config missing or invalid token");
                        return Task.FromResult(false);
                    }

                    try
                    {
                        JsonConvert.DeserializeObject(tokenMatch.Groups[1].Value.Trim());
                        DebugConsole.WriteSuccess($"{displayName} config validation passed");
                        return Task.FromResult(true);
                    }
                    catch (JsonException)
                    {
                        DebugConsole.WriteWarning("Config token is not valid JSON");
                        return Task.FromResult(false);
                    }
                }
                else
                {
                    DebugConsole.WriteSuccess($"{displayName} config validation passed");
                    return Task.FromResult(true);
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Config validation failed");
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Creates a new rclone config for the specified provider (opens browser for OAuth)
        /// </summary>
        public async Task<bool> CreateConfig(CloudProvider provider)
        {
            string providerName = _cloudProviderHelper.GetProviderConfigName(provider);
            string providerType = _cloudProviderHelper.GetProviderType(provider);
            string displayName = _cloudProviderHelper.GetProviderDisplayName(provider);

            DebugConsole.WriteSection($"Creating {displayName} Config");

            try
            {
                // Backup existing config if it exists
                if (File.Exists(_configPath))
                {
                    string backupPath = $"{_configPath}.backup_{DateTime.Now:yyyyMMdd_HHmmss}";
                    File.Move(_configPath, backupPath);
                    DebugConsole.WriteInfo($"Backed up existing config to: {backupPath}");
                }

                // Execute rclone config create command
                var result = await _rcloneExecutor.ExecuteRcloneCommand(
                    $"config create {providerName} {providerType} --config \"{_configPath}\"",
                    TimeSpan.FromMinutes(5),
                    false
                );

                // Validate the created config
                if (result.Success && await IsValidConfig(_configPath, provider))
                {
                    DebugConsole.WriteSuccess(
                        $"{displayName} configuration completed successfully"
                    );
                    return true;
                }
                else
                {
                    string errorMsg =
                        $"Rclone config failed. Exit code: {result.ExitCode}, Error: {result.Error}";
                    DebugConsole.WriteError(errorMsg);
                    return false;
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Config creation failed");
                return false;
            }
        }

        /// <summary>
        /// Tests the connection to the cloud provider
        /// </summary>
        public async Task<bool> TestConnection(CloudProvider provider)
        {
            string providerName = _cloudProviderHelper.GetProviderConfigName(provider);
            string displayName = _cloudProviderHelper.GetProviderDisplayName(provider);

            DebugConsole.WriteInfo($"Testing {displayName} connection...");

            try
            {
                var result = await _rcloneExecutor.ExecuteRcloneCommand(
                    $"lsd {providerName}: --max-depth 1 --config \"{_configPath}\"",
                    TimeSpan.FromSeconds(30)
                );

                if (result.Success)
                {
                    DebugConsole.WriteSuccess($"{displayName} connection test passed");
                    return true;
                }
                else
                {
                    DebugConsole.WriteWarning($"Connection test failed: {result.Error}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Connection test error");
                return false;
            }
        }

        /// <summary>
        /// Opens rclone config in interactive mode for manual setup
        /// </summary>
        public async Task<bool> OpenInteractiveConfig()
        {
            DebugConsole.WriteInfo("Opening rclone interactive config...");

            try
            {
                var result = await _rcloneExecutor.ExecuteRcloneCommand(
                    $"config --config \"{_configPath}\"",
                    TimeSpan.FromMinutes(10),
                    false // Don't hide console - user needs to interact
                );

                return result.Success;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to open interactive config");
                return false;
            }
        }

        /// <summary>
        /// Lists all configured remotes in the config file
        /// </summary>
        public async Task<string[]> ListRemotes()
        {
            DebugConsole.WriteInfo("Listing configured remotes...");

            try
            {
                var result = await _rcloneExecutor.ExecuteRcloneCommand(
                    $"listremotes --config \"{_configPath}\"",
                    TimeSpan.FromSeconds(10)
                );

                if (result.Success)
                {
                    var remotes = result.Output
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                    DebugConsole.WriteSuccess($"Found {remotes.Length} remote(s)");
                    return remotes;
                }
                else
                {
                    DebugConsole.WriteWarning("Failed to list remotes");
                    return Array.Empty<string>();
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Error listing remotes");
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Deletes a specific remote from the config
        /// </summary>
        public async Task<bool> DeleteRemote(string remoteName)
        {
            DebugConsole.WriteInfo($"Deleting remote: {remoteName}");

            try
            {
                var result = await _rcloneExecutor.ExecuteRcloneCommand(
                    $"config delete {remoteName} --config \"{_configPath}\"",
                    TimeSpan.FromSeconds(10)
                );

                if (result.Success)
                {
                    DebugConsole.WriteSuccess($"Remote '{remoteName}' deleted");
                    return true;
                }
                else
                {
                    DebugConsole.WriteError($"Failed to delete remote: {result.Error}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Error deleting remote");
                return false;
            }
        }
    }
}