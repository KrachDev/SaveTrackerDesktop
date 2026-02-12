using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.LOGIC;
using SaveTracker.Resources.Logic;
using SaveTracker.Resources.Logic.RecloneManagement;
using SaveTracker.Resources.SAVE_SYSTEM;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SaveTracker.Headless
{
    public class HeadlessGameService
    {
        private static HeadlessGameService? _instance;
        public static HeadlessGameService Instance => _instance ??= new HeadlessGameService();

        private GameProcessWatcher? _watcher;
        private SaveFileTrackerManager? _tracker;
        private CancellationTokenSource? _trackingCts;
        private Task? _trackingTask;
        private readonly SmartSyncService _smartSync;

        // Current State
        public bool IsTracking { get; private set; }
        public Game? CurrentGame { get; private set; }

        public HeadlessGameService()
        {
            _smartSync = new SmartSyncService();
        }

        public void Initialize(bool enableWatcher)
        {
            if (enableWatcher)
            {
                _watcher = new GameProcessWatcher();
                _watcher.GameProcessDetected += OnGameDetected;
                DebugConsole.WriteInfo("HeadlessGameService: Automatic Watchdog Enabled");
            }
            else
            {
                DebugConsole.WriteInfo("HeadlessGameService: Manual Mode (Watchdog Disabled)");
            }
        }

        public async Task StartTrackingAsync(Game game)
        {
            if (IsTracking)
            {
                DebugConsole.WriteWarning($"Already tracking {CurrentGame?.Name}. Stop first.");
                return;
            }

            CurrentGame = game;
            IsTracking = true;
            _trackingCts = new CancellationTokenSource();
            _tracker = new SaveFileTrackerManager();

            // Update IPC State (static, shared with CommandHandler)
            SaveTracker.Resources.LOGIC.IPC.CommandHandler.IsCurrentlyTracking = true;
            SaveTracker.Resources.LOGIC.IPC.CommandHandler.CurrentlyTrackingGame = game.Name;

            DebugConsole.WriteSuccess($"Starting tracking session for: {game.Name}");

            _trackingTask = Task.Run(async () =>
            {
                try
                {
                    await _tracker.Track(game);
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteException(ex, "Tracking session failed");
                }
            }, _trackingCts.Token);

            // If we have a watcher, mark as tracked to avoid double detection
            _watcher?.MarkGameAsTracked(game.Name);
        }

        public async Task StopTrackingAsync(Game game)
        {
            if (!IsTracking || CurrentGame?.Name != game.Name)
            {
                DebugConsole.WriteWarning("Not tracking this game.");
                return;
            }

            DebugConsole.WriteInfo($"Stopping tracking for {game.Name}...");

            _trackingCts?.Cancel();
            _tracker?.StopTracking();

            if (_trackingTask != null)
            {
                try
                {
                    await _trackingTask;
                }
                catch { /* Ignore cancellation */ }
            }

            IsTracking = false;
            CurrentGame = null;

            // Commit PlayTime
            game.LastTracked = DateTime.Now;
            await ConfigManagement.SaveGameAsync(game);

            // Auto Sync / Upload
            await PerformAutoSyncAsync(game);

            // Cleanup
            _watcher?.UnmarkGame(game.Name);
            SaveTracker.Resources.LOGIC.IPC.CommandHandler.IsCurrentlyTracking = false;
            SaveTracker.Resources.LOGIC.IPC.CommandHandler.CurrentlyTrackingGame = null;

            DebugConsole.WriteSuccess($"Session ended for {game.Name}");
        }

        private async Task PerformAutoSyncAsync(Game game)
        {
            try
            {
                DebugConsole.WriteInfo("Performing Smart Auto-Sync...");

                // Check if tracker has pending files
                var files = _tracker?.GetUploadList();
                if (files != null && files.Count > 0)
                {
                    DebugConsole.WriteInfo($"Found {files.Count} new files. Uploading directly...");
                    await UploadFilesAsync(game, files);
                    return;
                }

                // Fallback to Smart Sync check
                var provider = await _smartSync.GetEffectiveProvider(game);
                var comparison = await _smartSync.CompareProgressAsync(game, TimeSpan.FromSeconds(30), provider);

                if (comparison.Status == SmartSyncService.ProgressStatus.LocalAhead ||
                    comparison.Status == SmartSyncService.ProgressStatus.CloudNotFound)
                {
                    DebugConsole.WriteInfo($"Local is ahead ({comparison.Difference}). Uploading...");
                    // Re-get files just in case
                    files = _tracker?.GetUploadList();
                    if (files != null && files.Count > 0)
                        await UploadFilesAsync(game, files, force: comparison.Status == SmartSyncService.ProgressStatus.CloudNotFound);
                    else
                        DebugConsole.WriteWarning("Wanted to upload but no files found.");
                }
                else if (comparison.Status == SmartSyncService.ProgressStatus.CloudAhead)
                {
                    DebugConsole.WriteInfo($"Cloud is ahead ({comparison.Difference}). Downloading...");
                    // TODO: Implement Download Logic reuse (requires extraction from ViewModel or new Service method)
                    // For now, allow RcloneFileOperations to handle it
                    var rcloneOps = new SaveTracker.Resources.Logic.RecloneManagement.RcloneFileOperations(game);
                    string remotePath = await rcloneOps.GetRemotePathAsync(provider, game);
                    await rcloneOps.DownloadSelectedFilesAsync(remotePath, game, null); // Null = all files
                }
                else
                {
                    DebugConsole.WriteSuccess($"Sync Status: {comparison.Status}. No action needed.");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Auto-Sync failed");
            }
        }

        private async Task UploadFilesAsync(Game game, System.Collections.Generic.List<string> files, bool force = false)
        {
            var config = await ConfigManagement.LoadConfigAsync();
            var provider = config.CloudConfig.Provider;

            var installer = new SaveTracker.Resources.Logic.RecloneManagement.RcloneInstaller();
            var helper = new SaveTracker.Resources.Logic.CloudProviderHelper();
            var ops = new SaveTracker.Resources.Logic.RecloneManagement.RcloneFileOperations(game);

            var manager = new SaveTracker.Resources.Logic.SaveFileUploadManager(installer, helper, ops);
            await manager.Upload(files, game, provider, CancellationToken.None, force);
        }

        private async void OnGameDetected(Game game, int pid)
        {
            await StartTrackingAsync(game);
        }
    }
}
