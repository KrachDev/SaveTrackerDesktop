using CommunityToolkit.Mvvm.ComponentModel;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.Logic.RecloneManagement;
using SaveTracker.Resources.SAVE_SYSTEM;
using System;
using System.IO;

namespace SaveTracker.ViewModels
{
    // GameViewModel - Wraps Game model
    public partial class GameViewModel : ObservableObject
    {
        public Game Game { get; }

        [ObservableProperty]
        private string _name;

        [ObservableProperty]
        private string _installDirectory;

        [ObservableProperty]
        private Avalonia.Media.Imaging.Bitmap? _icon;

        [ObservableProperty]
        private bool _isDeleted;

        public GameViewModel(Game game)
        {
            Game = game;
            _name = game.Name;
            _installDirectory = game.InstallDirectory;
            _isDeleted = game.IsDeleted;

            try
            {
                _icon = Misc.ExtractIconFromExe(game.ExecutablePath);
            }
            catch
            {
                _icon = null;
            }
        }
    }

    // TrackedFileViewModel - Represents a tracked file
    public partial class TrackedFileViewModel : ObservableObject
    {
        private readonly FileChecksumRecord _record;
        private readonly Game _game;

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private string _fileName;

        [ObservableProperty]
        private string _path;

        [ObservableProperty]
        private string _size;

        [ObservableProperty]
        private string _lastModified;

        [ObservableProperty]
        private string _statusText;

        [ObservableProperty]
        private string _statusColor;

        public string AbsolutePath { get; }

        public TrackedFileViewModel(FileChecksumRecord record, Game game, string? lastSyncStatus)
        {
            _record = record;
            _game = game;

            AbsolutePath = record.GetAbsolutePath(game.InstallDirectory);
            _fileName = System.IO.Path.GetFileName(record.Path);
            _path = record.Path;
            _size = Misc.FormatFileSize(record.FileSize);
            _lastModified = record.LastUpload.ToString("MM/dd/yyyy HH:mm");

            var (statusText, statusColor) = Misc.GetStatusInfo(record, lastSyncStatus);
            _statusText = statusText;
            _statusColor = statusColor;
        }

        public void OpenFileLocation()
        {
            try
            {
                if (File.Exists(AbsolutePath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{AbsolutePath}\"",
                        UseShellExecute = true
                    });
                }
                else
                {
                    DebugConsole.WriteWarning($"File not found: {AbsolutePath}");
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to open file location");
            }
        }
    }

    // CloudFileViewModel - Represents a cloud file
    public partial class CloudFileViewModel : ObservableObject
    {
        private readonly RcloneFileInfo _fileInfo;

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private string _name;

        [ObservableProperty]
        private string _size;

        [ObservableProperty]
        private string _uploadDate;

        public CloudFileViewModel(RcloneFileInfo fileInfo)
        {
            _fileInfo = fileInfo;
            _name = fileInfo.Name;
            _size = Misc.FormatFileSize(fileInfo.Size);
            _uploadDate = fileInfo.ModTime.ToLocalTime().ToString("MM/dd/yyyy HH:mm");
        }
    }

    // RcloneFileInfo class (if not already defined elsewhere)
    public class RcloneFileInfo
    {
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
        public long Size { get; set; }
        public DateTime ModTime { get; set; }
        public bool IsDir { get; set; }
    }
}