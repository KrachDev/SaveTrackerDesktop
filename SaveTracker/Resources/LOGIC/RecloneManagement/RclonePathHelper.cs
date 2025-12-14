using System;
using System.IO;
using System.Runtime.InteropServices;

namespace SaveTracker.Resources.Logic.RecloneManagement
{
    public static class RclonePathHelper
    {
        public static string RcloneExePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtraTools", RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "rclone.exe" : "rclone");

        public static string ToolsPath => Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "ExtraTools"
        );

        /// <summary>
        /// Gets the config path for a specific cloud provider
        /// </summary>
        public static string GetConfigPath(CloudProvider provider)
        {
            string providerName = provider.ToString().ToLowerInvariant();
            return Path.Combine(ToolsPath, $"rclone_{providerName}.conf");
        }

        internal static string GetRclonePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtraTools", RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "rclone.exe" : "rclone");
        }

        /// <summary>
        /// Gets the legacy config path (for migration)
        /// </summary>
        public static string LegacyConfigPath => Path.Combine(ToolsPath, "rclone.conf");

        public static string GetRemotePath(CloudProvider provider)
        {
            var helper = new CloudProviderHelper();
            string configName = helper.GetProviderConfigName(provider);
            return $"{configName}:SaveTracker";
        }
    }
}

