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
                UseShellExecute = true
            };
            return Process.Start(startInfo);
        }

        private static Process? LaunchLinux(Game game)
        {
            // Simple heuristic for Linux:
            // 1. If it's a .exe, try to run with Wine.
            // 2. If it's a .sh or binary, run natively.
            // 3. TODO: Check for specific launcher configs (Steam/Lutris) in the future.

            string ext = Path.GetExtension(game.ExecutablePath).ToLower();
            var startInfo = new ProcessStartInfo
            {
                WorkingDirectory = game.InstallDirectory,
                UseShellExecute = false // We generally needed false to redirect stdout/err or change env, but true is often safer for xdg-open. Let's start with explicit commands.
            };

            if (ext == ".exe" || ext == ".bat" || ext == ".msi")
            {
                // Assume Wine
                DebugConsole.WriteInfo("Detected Windows executable on Linux - attempting to launch with Wine...");

                // Check if wine is installed
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
                startInfo.Arguments = $"\"{game.ExecutablePath}\"";
                startInfo.UseShellExecute = false;

                // If the user has a specific WINEPREFIX set in their environment, it will be respected.
                // We could allow per-game overrides in the future.
            }
            else
            {
                // Assume Native
                DebugConsole.WriteInfo("Detected Native executable/script on Linux...");

                // Determine valid filename
                if (ext == ".sh")
                {
                    // For shell scripts, it's often safer to run with /bin/bash if not executable
                    string? bashPath = LinuxUtils.FindExecutable("bash");
                    if (!string.IsNullOrEmpty(bashPath))
                    {
                        startInfo.FileName = bashPath;
                        startInfo.Arguments = $"\"{game.ExecutablePath}\"";
                        startInfo.UseShellExecute = false;
                    }
                    else
                    {
                        startInfo.FileName = game.ExecutablePath;
                        startInfo.UseShellExecute = true;
                    }
                }
                else
                {
                    startInfo.FileName = game.ExecutablePath;
                    startInfo.UseShellExecute = true; // Use shell execute for native apps to allow them to spawn properly in DE?
                }
            }

            try
            {
                return Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Failed to launch on Linux: {ex.Message}");
                // Fallback: try xdg-open?
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
