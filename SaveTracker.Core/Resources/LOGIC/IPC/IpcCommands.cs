using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Linq;
using SaveTracker.Resources.Logic.RecloneManagement;

namespace SaveTracker.Resources.LOGIC.IPC
{
    /// <summary>
    /// IPC Request from external addons (Playnite, etc.)
    /// </summary>
    public class IpcRequest
    {
        [JsonPropertyName("cmd")]
        public string Command { get; set; } = string.Empty;

        [JsonPropertyName("params")]
        public JsonElement? Params { get; set; }

        /// <summary>
        /// Helper to get a parameter value by name
        /// </summary>
        public T? GetParam<T>(string name, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        {
            if (Params == null || Params.Value.ValueKind == JsonValueKind.Undefined)
                return default;

            if (Params.Value.TryGetProperty(name, out var element))
            {
                return JsonSerializer.Deserialize<T>(element.GetRawText(), typeInfo);
            }
            return default;
        }

        /// <summary>
        /// Helper to get string parameter
        /// </summary>
        public string? GetString(string name)
        {
            if (Params == null || Params.Value.ValueKind == JsonValueKind.Undefined)
                return null;

            if (Params.Value.TryGetProperty(name, out var element))
            {
                return element.GetString();
            }
            return null;
        }
    }

    /// <summary>
    /// IPC Response to external addons
    /// </summary>
    public class IpcResponse
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Data { get; set; }

        [JsonPropertyName("error")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Error { get; set; }

        public static IpcResponse Success(object? data = null) => new() { Ok = true, Data = data };
        public static IpcResponse Fail(string error) => new() { Ok = false, Error = error };
    }

    /// <summary>
    /// Lightweight game info for IPC responses (subset of full Game class)
    /// </summary>
    public class GameInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("installDirectory")]
        public string InstallDirectory { get; set; } = string.Empty;

        [JsonPropertyName("executablePath")]
        public string ExecutablePath { get; set; } = string.Empty;

        [JsonPropertyName("lastTracked")]
        public DateTime LastTracked { get; set; }

        [JsonPropertyName("activeProfileId")]
        public string? ActiveProfileId { get; set; }

        [JsonPropertyName("steamAppId")]
        public string? SteamAppId { get; set; }

        [JsonPropertyName("trackedFilesCount")]
        public int TrackedFilesCount { get; set; }

        [JsonPropertyName("playTime")]
        public string PlayTime { get; set; } = "00:00:00";

        [JsonPropertyName("enableSmartSync")]
        public bool EnableSmartSync { get; set; }

        [JsonPropertyName("trackedFiles")]
        public List<string> TrackedFiles { get; set; } = new();

        public static GameInfo FromGame(Game game, GameUploadData? data = null) => new()
        {
            Name = game.Name,
            InstallDirectory = game.InstallDirectory,
            ExecutablePath = game.ExecutablePath,
            LastTracked = game.LastTracked,
            ActiveProfileId = game.ActiveProfileId ?? "default",
            SteamAppId = game.SteamAppId,
            TrackedFilesCount = data?.Files?.Count ?? 0,
            PlayTime = data?.PlayTime.ToString(@"hh\:mm\:ss") ?? "00:00:00",
            EnableSmartSync = data?.EnableSmartSync ?? true,
            TrackedFiles = data?.Files?.Values.Select(f => f.Path).ToList() ?? new()
        };
    }

    /// <summary>
    /// Profile info for IPC responses
    /// </summary>
    public class ProfileInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("isDefault")]
        public bool IsDefault { get; set; }

        public static ProfileInfo FromProfile(Profile profile) => new()
        {
            Id = profile.Id,
            Name = profile.Name,
            IsDefault = profile.IsDefault
        };
    }

    /// <summary>
    /// Cloud provider info for IPC responses
    /// </summary>
    public class ProviderInfo
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Rclone status info
    /// </summary>
    public class RcloneStatus
    {
        [JsonPropertyName("installed")]
        public bool Installed { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("needsUpdate")]
        public bool NeedsUpdate { get; set; }
    }

    /// <summary>
    /// Sync/tracking status info
    /// </summary>
    public class SyncStatus
    {
        [JsonPropertyName("isTracking")]
        public bool IsTracking { get; set; }

        [JsonPropertyName("trackingGame")]
        public string? TrackingGame { get; set; }

        [JsonPropertyName("isUploading")]
        public bool IsUploading { get; set; }

        [JsonPropertyName("isDownloading")]
        public bool IsDownloading { get; set; }

        [JsonPropertyName("currentOperation")]
        public string? CurrentOperation { get; set; }
    }

    /// <summary>
    /// Game status info (has data, in cloud, etc.)
    /// </summary>
    public class GameStatus
    {
        [JsonPropertyName("hasData")]
        public bool HasData { get; set; }

        [JsonPropertyName("lastTracked")]
        public DateTime? LastTracked { get; set; }

        [JsonPropertyName("isInCloud")]
        public bool IsInCloud { get; set; }

        [JsonPropertyName("localPlayTime")]
        public string? LocalPlayTime { get; set; }

        [JsonPropertyName("cloudPlayTime")]
        public string? CloudPlayTime { get; set; }
    }

    /// <summary>
    /// Progress comparison result
    /// </summary>
    public class ProgressComparisonResult
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("localPlayTime")]
        public string? LocalPlayTime { get; set; }

        [JsonPropertyName("cloudPlayTime")]
        public string? CloudPlayTime { get; set; }

        [JsonPropertyName("recommendation")]
        public string? Recommendation { get; set; }
    }
}
