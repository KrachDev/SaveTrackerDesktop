using CommunityToolkit.Mvvm.ComponentModel;
using SaveTracker.Resources.Logic.RecloneManagement;
using System;
using System.Collections.Generic;

namespace SaveTracker.Models
{
    /// <summary>
    /// Represents a game using the legacy cloud save format
    /// </summary>
    public partial class LegacyGameItem : ObservableObject
    {
        [ObservableProperty]
        private string _name = "";

        [ObservableProperty]
        private List<string> _legacyFiles = new();

        [ObservableProperty]
        private int _fileCount;

        [ObservableProperty]
        private long _totalSize;

        [ObservableProperty]
        private bool _hasNewFormat;

        [ObservableProperty]
        private bool _isSelected = true;

        [ObservableProperty]
        private MigrationStatus _status = MigrationStatus.None;

        [ObservableProperty]
        private ConflictResolution _conflictResolution = ConflictResolution.KeepNew;

        [ObservableProperty]
        private string _statusMessage = "";

        [ObservableProperty]
        private DateTime? _lastUpdated;

        [ObservableProperty]
        private GameUploadData? _legacyChecksum;

        public string StatusText => Status switch
        {
            MigrationStatus.None => HasNewFormat ? "⚠ Conflict" : "Ready",
            MigrationStatus.InProgress => "Migrating...",
            MigrationStatus.Completed => "✓ Migrated",
            MigrationStatus.Failed => "✗ Failed",
            MigrationStatus.Skipped => "Skipped",
            _ => "Unknown"
        };

        public string StatusColor => Status switch
        {
            MigrationStatus.None => HasNewFormat ? "#F0AD4E" : "#4CC9B0",
            MigrationStatus.InProgress => "#007ACC",
            MigrationStatus.Completed => "#4CC9B0",
            MigrationStatus.Failed => "#D9534F",
            MigrationStatus.Skipped => "#858585",
            _ => "#CCCCCC"
        };

        public string ConflictText => HasNewFormat
            ? $"Has both formats - {ConflictResolution}"
            : "None";

        public string FileSizeText => FormatBytes(TotalSize);

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }
    }

    public enum MigrationStatus
    {
        None,
        InProgress,
        Completed,
        Failed,
        Skipped
    }

    public enum ConflictResolution
    {
        KeepNew,    // Ignore old files, keep new format
        Merge,      // Add missing old files to new format
        ReplaceOld  // Overwrite new with old (destructive)
    }
}
