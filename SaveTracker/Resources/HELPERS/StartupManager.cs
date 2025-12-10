using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace SaveTracker.Resources.HELPERS
{
    public static class StartupManager
    {
        private const string TaskName = "SaveTrackerDesktop";

        public static void SetStartup(bool enable)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    if (enable)
                    {
                        CreateStartupTaskWindows();
                    }
                    else
                    {
                        DeleteStartupTaskWindows();
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    if (enable)
                    {
                        CreateStartupTaskLinux();
                    }
                    else
                    {
                        DeleteStartupTaskLinux();
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to change startup settings");
            }
        }

        private static void CreateStartupTaskWindows()
        {
            try
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    DebugConsole.WriteError("Failed to get executable path");
                    return;
                }

                // First, delete any existing task
                DeleteStartupTaskWindows();

                // Create XML task definition with highest privileges
                string taskXml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>SaveTracker Desktop - Automatic game save backup</Description>
    <Author>{Environment.UserName}</Author>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{Environment.UserDomainName}\\{Environment.UserName}</UserId>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{Environment.UserDomainName}\\{Environment.UserName}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{exePath}</Command>
    </Exec>
  </Actions>
</Task>";

                // Save XML to temp file
                string tempXmlPath = Path.Combine(Path.GetTempPath(), $"{TaskName}.xml");
                File.WriteAllText(tempXmlPath, taskXml);

                // Use schtasks.exe to create the task
                var startInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Create /TN \"{TaskName}\" /XML \"{tempXmlPath}\" /F",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process != null)
                    {
                        process.WaitForExit();
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();

                        if (process.ExitCode == 0)
                        {
                            DebugConsole.WriteSuccess($"Added {TaskName} to startup (will run with admin rights)");
                        }
                        else
                        {
                            DebugConsole.WriteError($"Failed to create startup task: {error}");
                        }
                    }
                }

                // Clean up temp file
                try
                {
                    if (File.Exists(tempXmlPath))
                        File.Delete(tempXmlPath);
                }
                catch { }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to create startup task");
            }
        }



        private static void CreateStartupTaskLinux()
        {
            try
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    DebugConsole.WriteError("Failed to get executable path");
                    return;
                }

                string autostartDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "autostart");
                // On Linux, Environment.SpecialFolder.ApplicationData keeps returning ~/.config which is correct for XDG

                if (!Directory.Exists(autostartDir))
                {
                    Directory.CreateDirectory(autostartDir);
                }

                string desktopFilePath = Path.Combine(autostartDir, "savetracker.desktop");

                string desktopFileContent = $"""
[Desktop Entry]
Type=Application
Name=SaveTracker Desktop
Comment=Automatic game save backup
Exec="{exePath}"
Terminal=false
Categories=Utility;Game;
""";

                File.WriteAllText(desktopFilePath, desktopFileContent);
                DebugConsole.WriteSuccess($"Created Linux autostart entry: {desktopFilePath}");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to create Linux startup entry");
            }
        }

        private static void DeleteStartupTaskWindows()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Delete /TN \"{TaskName}\" /F",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process != null)
                    {
                        process.WaitForExit();
                        if (process.ExitCode == 0)
                        {
                            DebugConsole.WriteSuccess($"Removed {TaskName} from startup");
                        }
                        // If exit code is 1 (task doesn't exist), that's fine - don't log an error
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to delete startup task");
            }
        }

        private static void DeleteStartupTaskLinux()
        {
            try
            {
                string autostartDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "autostart");
                string desktopFilePath = Path.Combine(autostartDir, "savetracker.desktop");

                if (File.Exists(desktopFilePath))
                {
                    File.Delete(desktopFilePath);
                    DebugConsole.WriteSuccess("Removed Linux autostart entry");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to remove Linux startup entry");
            }
        }

        public static bool IsStartupEnabled()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "schtasks.exe",
                        Arguments = $"/Query /TN \"{TaskName}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using (var process = Process.Start(startInfo))
                    {
                        if (process != null)
                        {
                            process.WaitForExit();
                            return process.ExitCode == 0;
                        }
                    }
                    return false;
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    string autostartDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "autostart");
                    string desktopFilePath = Path.Combine(autostartDir, "savetracker.desktop");
                    return File.Exists(desktopFilePath);
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
