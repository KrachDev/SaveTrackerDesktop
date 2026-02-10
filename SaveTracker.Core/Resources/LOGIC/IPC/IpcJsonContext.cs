using System.Text.Json.Serialization;
using SaveTracker.Models;

namespace SaveTracker.Resources.LOGIC.IPC
{
    [JsonSerializable(typeof(IpcRequest))]
    [JsonSerializable(typeof(IpcResponse))]
    [JsonSerializable(typeof(Game))]
    [JsonSerializable(typeof(PingResponse))]

    [JsonSerializable(typeof(TrackingStatusResponse))]
    [JsonSerializable(typeof(GameAddedResponse))]
    [JsonSerializable(typeof(GameDeletedResponse))]
    [JsonSerializable(typeof(CloudPresenceResponse))]
    [JsonSerializable(typeof(ProfileChangedResponse))]
    [JsonSerializable(typeof(ProviderSetResponse))]
    [JsonSerializable(typeof(ConfiguredResponse))]
    [JsonSerializable(typeof(SyncTriggeredResponse))]
    [JsonSerializable(typeof(UploadStartedResponse))]
    [JsonSerializable(typeof(GlobalSettingsResponse))]
    [JsonSerializable(typeof(SavedResponse))]
    [JsonSerializable(typeof(GameSettingsResponse))]
    [JsonSerializable(typeof(WindowShownResponse))]
    [JsonSerializable(typeof(IssueReportedResponse))]
    [JsonSerializable(typeof(InstallRcloneResponse))]
    [JsonSerializable(typeof(SelectedProviderResponse))]
    
    // New types
    [JsonSerializable(typeof(SyncStatusResponse))]
    [JsonSerializable(typeof(ProfileListResponse))]
    [JsonSerializable(typeof(ActiveProfileResponse))]
    [JsonSerializable(typeof(ProvidersResponse))]
    [JsonSerializable(typeof(CompareProgressResponse))]
    
    // Common collections and primitives
    [JsonSerializable(typeof(System.Collections.Generic.List<Game>))]
    [JsonSerializable(typeof(System.Collections.Generic.List<string>))]
    [JsonSerializable(typeof(int))]
    [JsonSerializable(typeof(int?))]
    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(string))]
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
    public partial class IpcJsonContext : JsonSerializerContext
    {
    }
}
