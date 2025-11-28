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

        public static string ConfigPath => Path.Combine(ToolsPath, "rclone.conf");
    }
}
