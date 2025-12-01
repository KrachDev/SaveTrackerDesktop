using System;
using System.IO;

namespace SaveTracker.Resources.Logic.RecloneManagement
{
    public static class RclonePathHelper
    {
        public static string RcloneExePath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtraTools", "rclone.exe");

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

        /// <summary>
        /// Gets the legacy config path (for migration)
        /// </summary>
        public static string LegacyConfigPath => Path.Combine(ToolsPath, "rclone.conf");
    }
}
