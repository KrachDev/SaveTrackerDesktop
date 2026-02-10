using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SaveTracker.Resources.LOGIC.IPC
{
    public class PingResponse
    {
        public bool Pong { get; set; }
    }

    public class TrackingStatusResponse
    {
        public bool Tracking { get; set; }
        public string? GameName { get; set; }
    }

    public class GameAddedResponse
    {
        public bool Added { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class GameDeletedResponse
    {
        public bool Deleted { get; set; }
    }

    public class CloudPresenceResponse
    {
        public bool InCloud { get; set; }
    }

    public class ProfileChangedResponse
    {
        public bool Changed { get; set; }
        public string NewProfileId { get; set; } = string.Empty;
    }

    public class ProviderSetResponse
    {
        public bool Set { get; set; }
        public string Provider { get; set; } = string.Empty;
    }

    public class ConfiguredResponse
    {
        public bool Configured { get; set; }
        public string? Provider { get; set; }
    }

    public class SyncTriggeredResponse
    {
        public bool Triggered { get; set; }
        public string GameName { get; set; } = string.Empty;
    }

    public class UploadStartedResponse
    {
        public bool UploadStarted { get; set; }
        public string GameName { get; set; } = string.Empty;
    }

    public class GlobalSettingsResponse
    {
        public bool EnableAutomaticTracking { get; set; }
        public bool TrackWrite { get; set; }
        public bool TrackReads { get; set; }
        public bool AutoUpload { get; set; }
        public bool StartMinimized { get; set; }
        public bool ShowDebugConsole { get; set; }
        public bool EnableNotifications { get; set; }
        public bool CheckForUpdatesOnStartup { get; set; }
        public bool EnableAnalytics { get; set; }
        public string CloudProvider { get; set; } = string.Empty;
    }

    public class SavedResponse
    {
        public bool Saved { get; set; }
    }

    public class GameSettingsResponse
    {
        public bool HasSettings { get; set; }
        public bool? EnableSmartSync { get; set; }
        public string? GameProvider { get; set; }
        public string? PlayTime { get; set; }
        public int? FilesCount { get; set; }
    }

    public class WindowShownResponse
    {
        public bool Shown { get; set; }
        public bool Triggered { get; set; }
        public string? Window { get; set; }
    }

    public class IssueReportedResponse
    {
        public bool Opened { get; set; }
    }

    public class InstallRcloneResponse
    {
        public bool Installed { get; set; }
    }


    public class SelectedProviderResponse
    {
        public int Provider { get; set; }
        public string Name { get; set; } = string.Empty;
    //    public string DisplayName { get; set; } = string.Empty;
    }

    public class SyncStatusResponse
    {
        public bool IsUploading { get; set; }
        public bool IsDownloading { get; set; }
        public string? CurrentOperation { get; set; }
    }

    public class ProfileListResponse
    {
        public List<string> Profiles { get; set; } = new();
        public string ActiveProfileId { get; set; } = string.Empty;
    }

    public class ActiveProfileResponse
    {
        public string ActiveProfileId { get; set; } = string.Empty;
    }


    public class ProvidersResponse
    {
        public List<string> Providers { get; set; } = new();
    }

    public class CompareProgressResponse
    {
        public string Status { get; set; } = string.Empty;
        public string LocalTime { get; set; } = string.Empty;
        public string CloudTime { get; set; } = string.Empty;
    }
}
