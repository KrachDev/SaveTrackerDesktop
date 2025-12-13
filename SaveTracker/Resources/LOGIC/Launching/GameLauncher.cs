using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.SAVE_SYSTEM; // For Game class

namespace SaveTracker.Resources.LOGIC.Launching
{
    public static class GameLauncher
    {
        public static Process? Launch(Game game)
        {
            if (game == null || string.IsNullOrEmpty(game.ExecutablePath))
                throw new ArgumentNullException(nameof(game));

            if (!File.Exists(game.ExecutablePath))
                throw new FileNotFoundException($"Executable not found at {game.ExecutablePath}");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return LaunchWindows(game);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return LaunchLinux(game);
            }
            else
            {
                throw new PlatformNotSupportedException("OS not supported for launching.");
            }
        }

        private static Process? LaunchWindows(Game game)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = game.ExecutablePath,
                WorkingDirectory = game.InstallDirectory,
                UseShellExecute = true,
                Arguments = game.LaunchArguments
            };
            return Process.Start(startInfo);
        }

        private static Process? LaunchLinux(Game game)
        {
            var startInfo = new ProcessStartInfo
            {
                WorkingDirectory = game.InstallDirectory,
                UseShellExecute = false
            };

            // Use Linux-specific arguments
            string args = game.LinuxArguments ?? "";

            // 1. Custom Linux Wrapper (User override)
            if (!string.IsNullOrEmpty(game.LinuxLaunchWrapper))
            {
                DebugConsole.WriteInfo($"Launching with custom wrapper: {game.LinuxLaunchWrapper}");

                // Heuristic to split command and arguments
                string[] parts = game.LinuxLaunchWrapper.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    string runner = parts[0];
                    string runnerArgs = parts.Length > 1 ? parts[1] : "";

                    startInfo.FileName = runner;
                    // Format: [RunnerArgs] "[GameExe]" [GameArgs]
                    startInfo.Arguments = $"{runnerArgs} \"{game.ExecutablePath}\" {args}".Trim();

                    try
                    {
                        return Process.Start(startInfo);
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteError($"Failed to launch via wrapper '{runner}': {ex.Message}");
                        throw;
                    }
                }
            }

            // 2. Default Auto-Detection
            string ext = Path.GetExtension(game.ExecutablePath).ToLower();

            if (ext == ".exe" || ext == ".bat" || ext == ".msi")
            {
                // Assume Wine
                DebugConsole.WriteInfo("Detected Windows executable on Linux - attempting to launch with Wine...");

                string? winePath = LinuxUtils.FindExecutable("wine");
                if (string.IsNullOrEmpty(winePath))
                {
                    DebugConsole.WriteWarning("Wine not found in PATH. Launching with 'wine' command might fail.");
                    winePath = "wine"; // Try default
                }
                else
                {
                    DebugConsole.WriteDebug($"Found wine at: {winePath}");
                }

                startInfo.FileName = winePath;
                startInfo.Arguments = $"\"{game.ExecutablePath}\" {args}".Trim();
                startInfo.UseShellExecute = false;
            }
            else
            {
                // Native
                DebugConsole.WriteInfo("Detected Native executable/script on Linux...");

                // Determine valid filename
                if (ext == ".sh")
                {
                    string? bashPath = LinuxUtils.FindExecutable("bash");
                    if (!string.IsNullOrEmpty(bashPath))
                    {
                        startInfo.FileName = bashPath;
                        startInfo.Arguments = $"\"{game.ExecutablePath}\" {args}".Trim();
                        startInfo.UseShellExecute = false;
                    }
                    else
                    {
                        startInfo.FileName = game.ExecutablePath;
                        startInfo.Arguments = args;
                        startInfo.UseShellExecute = true;
                    }
                }
                else
                {
                    startInfo.FileName = game.ExecutablePath;
                    startInfo.Arguments = args;
                    startInfo.UseShellExecute = true; // Use shell execute for native apps
                }
            }

            try
            {
                return Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Failed to launch on Linux: {ex.Message}");
                // Fallback: try xdg-open
                try
                {
                    DebugConsole.WriteInfo("Attempting fallback with xdg-open...");
                    var fallbackParams = new ProcessStartInfo
                    {
                        FileName = "xdg-open",
                        Arguments = $"\"{game.ExecutablePath}\"",
                        UseShellExecute = true
                    };
                    return Process.Start(fallbackParams);
                }
                catch
                {
                    throw ex; // Throw original exception if fallback fails
                }
            }
        }
    }
}
