using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.LOGIC.IPC;

namespace SaveTracker.Headless
{
    internal class Program
    {
        private static readonly CancellationTokenSource _cts = new();

        static async Task Main(string[] args)
        {
            DebugConsole.Enable(true);
            DebugConsole.WriteSection("SaveTracker Headless Mode");

            if (args.Contains("--test-sta"))
            {
                await SaveTracker.Resources.Logic.Testing.SaveArchiverTestChamber.RunTest();
                return;
            }

            // Handle CTRL+C
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                _cts.Cancel();
                DebugConsole.WriteInfo("Stopping...");
            };

            try
            {
                // Initialize Config (Loads or creates defaults)
                await SaveTracker.Resources.SAVE_SYSTEM.ConfigManagement.LoadConfigAsync();

                // Initialize Game Service (Core Logic)
                bool enableWatcher = args.Contains("--enable-watcher");
                HeadlessGameService.Instance.Initialize(enableWatcher);

                // Start IPC Server
                var windowManager = new HeadlessWindowManager();

                // Fire and forget server task, linked to CTS
                var serverTask = IpcServer.StartAsync(windowManager, _cts.Token, ignoreConfig: true);
                DebugConsole.WriteSuccess("Ready to accept commands.");

                // Keep alive until cancelled
                try
                {
                    await Task.Delay(-1, _cts.Token);
                }
                catch (TaskCanceledException)
                {
                    // Shutdown requested
                }

                await serverTask;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Critical Error in Headless Mode");
            }
            finally
            {
                // Gracefully stop the IPC server
                IpcServer.Stop();
                DebugConsole.WriteInfo("Exiting.");
            }
        }
    }
}
