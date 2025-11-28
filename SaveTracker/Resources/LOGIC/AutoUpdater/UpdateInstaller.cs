using SaveTracker.Resources.HELPERS;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace SaveTracker.Resources.Logic.AutoUpdater
{
    /// <summary>
    /// Service to install updates by replacing the running executable
    /// </summary>
    public class UpdateInstaller
    {
        private static readonly string CurrentExePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
        private static readonly string UpdateCachePath = UpdateDownloader.GetCachePath();

        /// <summary>
        /// Installs the update by creating and launching a batch script
        /// </summary>
        /// <param name="downloadedFilePath">Path to the downloaded update executable</param>
        public async Task InstallUpdateAsync(string downloadedFilePath)
        {
            DebugConsole.WriteSection("Installing Update");
            DebugConsole.WriteKeyValue("Current Exe", CurrentExePath);
            DebugConsole.WriteKeyValue("Update File", downloadedFilePath);

            try
            {
                // Validate paths
                if (string.IsNullOrEmpty(CurrentExePath))
                {
                    throw new Exception("Could not determine current executable path");
                }

                if (!File.Exists(downloadedFilePath))
                {
                    throw new Exception($"Update file not found: {downloadedFilePath}");
                }

                // Create the batch script
                string batchScriptPath = CreateUpdateBatchScript(downloadedFilePath);
                DebugConsole.WriteInfo($"Created batch script: {batchScriptPath}");

                // Launch the batch script
                var processInfo = new ProcessStartInfo
                {
                    FileName = batchScriptPath,
                    UseShellExecute = true,
                    CreateNoWindow = false, // Show the CMD window
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };

                DebugConsole.WriteInfo("Launching update script...");
                Process.Start(processInfo);

                DebugConsole.WriteSuccess("Update script launched successfully");
                DebugConsole.WriteInfo("Application will now exit...");

                // Give the batch script a moment to start
                await Task.Delay(500);

                // Exit the application - the batch script will handle the rest
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to install update");
                throw;
            }
        }

        /// <summary>
        /// Creates a batch script to handle the update process
        /// </summary>
        /// <param name="newExePath">Path to the new executable</param>
        /// <returns>Path to the created batch script</returns>
        private string CreateUpdateBatchScript(string newExePath)
        {
            string batchScriptPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "update_installer.bat"
            );

            // Get the process name without extension
            string processName = Path.GetFileNameWithoutExtension(CurrentExePath);

            // Create the batch script content
            string batchContent = $@"@echo off
echo SaveTracker Auto-Updater
echo ========================
echo.
echo Waiting for application to close...
timeout /t 2 /nobreak >nul

echo Terminating any remaining processes...
taskkill /F /IM ""{processName}.exe"" >nul 2>&1

echo Waiting for file unlock...
timeout /t 1 /nobreak >nul

echo Replacing executable...
copy /Y ""{newExePath}"" ""{CurrentExePath}""

if errorlevel 1 (
    echo ERROR: Failed to replace executable!
    echo Press any key to exit...
    pause >nul
    exit /b 1
)

echo Cleaning up cache...
rmdir /S /Q ""{UpdateCachePath}"" >nul 2>&1

echo Deleting update script...
del ""{batchScriptPath}"" >nul 2>&1

echo Starting updated application...
start """" ""{CurrentExePath}""

echo Update completed successfully!
timeout /t 2 /nobreak >nul
exit
";

            // Write the batch script
            File.WriteAllText(batchScriptPath, batchContent);

            return batchScriptPath;
        }
    }
}
