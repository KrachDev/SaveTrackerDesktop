# SaveTracker IPC API

This document describes the named-pipe IPC API exposed by SaveTracker (Headless helper). It lists the pipe name, request/response format, available commands, parameters, response DTOs and usage examples.

**Pipe name**
- `SaveTracker_Command_Pipe` (Windows named pipe: `\\.\pipe\SaveTracker_Command_Pipe`)

**Transport**
- UTF-8 JSON messages sent over a bidirectional Named Pipe.
- Each request is a single JSON object terminated with newline. Server responds with a single JSON object (terminated with newline).

**Request format**
- JSON object with these fields:
  - `cmd` (string) — command name (case-insensitive).
  - `params` (object) — optional parameters for the command.

Example request:
```json
{"cmd":"getgame","params":{"name":"My Game"}}
```

Note: The server deserializes using `IpcRequest` and helpers `GetString()` / `GetParam<T>()` are available to handlers.

**Response format**
- JSON object shaped as `IpcResponse`:
  - `ok` (bool) — true when successful.
  - `data` (object) — command-specific payload (present when `ok` is true).
  - `error` (string) — error message (present when `ok` is false).

Examples:
- Success: { "ok": true, "data": { ... } }
- Failure: { "ok": false, "error": "Missing 'name' parameter" }


**High-level notes**
- Commands are handled by `CommandHandler` (SaveTracker.Core.Resources.LOGIC.IPC.CommandHandler).
- The server enforces a maximum response size and will return an error response if the payload is too large.
- The server supports concurrent clients via a small pool of NamedPipeServerStream instances.


**Commands**
Below are the commands implemented in `CommandHandler` (grouped by category). Commands are compared case-insensitively.

- STATUS COMMANDS
  - `ping` — Check if IPC server is alive.
    - Request: `{ "cmd": "ping" }`
    - Response data: `PingResponse { pong: true }`
    - Example success: `{ "ok": true, "data": { "pong": true } }`

  - `help` — Returns categorized help (list of all commands and descriptions).
    - Response data: `HelpResponse { categories: [ { name, commands: [{command, description, params}] } ] }`

  - `istracking` — Check if any game is currently being tracked.
    - Response data: `TrackingStatusResponse { tracking: bool, gameName: string? }`

  - `getsyncstatus` — Get current upload/download status.
    - Response data: `SyncStatusResponse { isUploading: bool, isDownloading: bool, currentOperation: string? }`


- GAME COMMANDS
  - `getgamelist` — Returns list of all saved games.
    - Request: `{ "cmd": "getgamelist" }`
    - Response data: list of `Game` or `GameInfo` objects (serialized subset).

  - `getgame` — Get details for a specific game.
    - Request: `{ "cmd": "getgame", "params": { "name": "My Game" } }`
    - Response data: `Game` object or fail if not found.

  - `addgame` — Add or update a game.
    - Request: `{ "cmd":"addgame", "params": { <game object JSON> } }`
    - The handler expects a full game object JSON in `params` (serialized as SaveTracker `Game`).
    - Response: `GameAddedResponse { added: true, name: "..." }` on success.

  - `deletegame` — Remove a game.
    - Request: `{ "cmd":"deletegame", "params": { "name": "My Game" } }`
    - Response: `GameDeletedResponse { deleted: true }`

  - `getgamestatus` — Get tracking status for a specific game.
    - Request: `{ "cmd":"getgamestatus", "params": { "name": "My Game" } }`
    - Response: `TrackingStatusResponse` (tracking boolean and name).

  - `checkcloudpresence` — Check whether the game has cloud saves.
    - Request: `{ "cmd":"checkcloudpresence", "params": { "name": "My Game" } }`
    - Response: `CloudPresenceResponse { inCloud: bool }`


- PROFILE COMMANDS
  - `getprofiles` — Returns profiles for a game.
    - Request: `{ "cmd":"getprofiles", "params": { "name": "My Game" } }`
    - Response: `ProfileListResponse { profiles: [string], activeProfileId: string }` (may be stubbed to default currently).

  - `getactiveprofile` — Get active profile id for a game.
    - Request: `{ "cmd":"getactiveprofile", "params": { "name": "My Game" } }`
    - Response: `ActiveProfileResponse { activeProfileId: string }`

  - `changeprofile` — Switch active profile.
    - Request: `{ "cmd":"changeprofile", "params": { "name":"My Game", "profileId":"id" } }`
    - Response: `ProfileChangedResponse { changed: true, newProfileId: "id" }`


- CLOUD / RCLONE COMMANDS
  - `getproviders` — List configured provider names.
    - Response: `ProvidersResponse { providers: [string] }`

  - `getselectedprovider` — Get currently selected provider.
    - Response: `SelectedProviderResponse { provider: int, name: string }`

  - `setprovider` — Set active provider.
    - Request: `{ "cmd":"setprovider", "params": { "provider": 1 } }` (provider is numeric enum)
    - Response: `ProviderSetResponse { set: true, provider: "GoogleDrive" }`

  - `getrclonestatus` — Check if rclone is installed.
    - Response: `InstallRcloneResponse { installed: bool }`

  - `installrclone` — Trigger rclone install/check.
    - Response: `InstallRcloneResponse { installed: bool }`

  - `isproviderconfigured` — Check provider config.
    - Request: `{ "cmd":"isproviderconfigured", "params": { "provider": 1 } }`
    - Response: `ConfiguredResponse { configured: bool, provider: string }`

  - `configureprovider` — Attempt to create provider config (rclone config creation).
    - Request: `{ "cmd":"configureprovider", "params": { "provider": 1 } }`
    - Response: `ConfiguredResponse { configured: true }` or fail with message.


- SYNC COMMANDS
  - `triggersync` — Trigger a SmartSync for the specified game.
    - Request: `{ "cmd":"triggersync", "params": { "name":"My Game" } }`
    - Response: `SyncTriggeredResponse { triggered: true, gameName: "My Game" }`

  - `uploadnow` — Start an immediate upload for the game.
    - Request: `{ "cmd":"uploadnow", "params": { "name":"My Game" } }`
    - Response: `UploadStartedResponse { uploadStarted: true, gameName: "My Game" }`

  - `compareprogress` — Compare local vs cloud playtime/progress.
    - Request: `{ "cmd":"compareprogress", "params": { "name":"My Game" } }`
    - Response: `CompareProgressResponse { status, localTime, cloudTime }`


- SETTINGS COMMANDS
  - `getsettings` — Get global settings.
    - Response: `GlobalSettingsResponse` with fields like `enableAutomaticTracking`, `autoUpload`, `cloudProvider`, etc.

  - `savesettings` — Update global settings.
    - Request example: `{ "cmd":"savesettings", "params": { "enableAutomaticTracking": true, "autoUpload": false } }`
    - Response: `SavedResponse { saved: true }`

  - `getgamesettings` — Get per-game saved settings.
    - Request: `{ "cmd":"getgamesettings", "params": { "name":"My Game" } }`
    - Response: `GameSettingsResponse` (hasSettings, playTime, filesCount, etc.)

  - `savegamesettings` — Save per-game settings.
    - Request: `{ "cmd":"savegamesettings", "params": { "name":"My Game", "enableSmartSync": true } }`
    - Response: `SavedResponse { saved: true }`


- WINDOW COMMANDS (GUI-only; headless logs warnings)
  - `showmainwindow`, `showlibrary`, `showblacklist`, `showcloudsettings`, `showsettings` — instruct UI to show specific windows.
    - Request example: `{ "cmd":"showlibrary" }`
    - Response: `WindowShownResponse { shown: true }` (headless prints warning).

  - `reportissue` — Open issue reporting / browser.
    - Response: `IssueReportedResponse { opened: true }`


- SESSION COMMANDS
  - `startsession` — Manually start tracking a game (equivalent to starting tracking service for that game).
    - Request: `{ "cmd":"startsession", "params": { "name":"My Game" } }`
    - Response: `{ ok: true, data: { started: true, game: "My Game" } }`

  - `endsession` / `stopsession` — Stop tracking and perform any uploads.
    - Request: `{ "cmd":"endsession", "params": { "name":"My Game" } }`
    - Response: `{ ok: true, data: { ended: true, game: "My Game" } }`


- DATA / BLACKLIST / LIBRARY
  - `getblacklist` — Returns current blacklist config.
    - Response: `BlacklistResponse` with lists: directories, extensions, filenames, keywords.

  - `addblacklist` — Add item to blacklist.
    - Request: `{ "cmd":"addblacklist", "params": { "type": "extension", "value": ".tmp" } }`
    - Response: `BlacklistActionResponse { success: bool, message: string }`

  - `removeblacklist` — Remove item from blacklist.
    - Request: `{ "cmd":"removeblacklist", "params": { "type":"extension","value":".tmp" } }`

  - `getcloudlibrary` — Returns merged list of local/cloud library items with stats.


**Common DTOs (summary)**
- `PingResponse` { pong: bool }
- `TrackingStatusResponse` { tracking: bool, gameName?: string }
- `GameAddedResponse` { added: bool, name: string }
- `GameDeletedResponse` { deleted: bool }
- `CloudPresenceResponse` { inCloud: bool }
- `ProviderSetResponse` { set: bool, provider: string }
- `ConfiguredResponse` { configured: bool, provider?: string }
- `SyncTriggeredResponse` { triggered: bool, gameName: string }
- `UploadStartedResponse` { uploadStarted: bool, gameName: string }
- `GlobalSettingsResponse` { enableAutomaticTracking, trackWrite, trackReads, autoUpload, startMinimized, showDebugConsole, enableNotifications, checkForUpdatesOnStartup, enableAnalytics, cloudProvider }
- `SavedResponse` { saved: bool }
- `GameSettingsResponse` { hasSettings: bool, enableSmartSync?: bool, gameProvider?: string, playTime?: string, filesCount?: int }
- `WindowShownResponse` { shown?: bool, triggered?: bool, window?: string }
- `CompareProgressResponse` { status: string, localTime: string, cloudTime: string }
- `BlacklistResponse` { directories: [string], extensions:[string], fileNames:[string], keywords:[string] }
- `BlacklistActionResponse` { success: bool, message: string }


**Examples**

- PowerShell (test_ipc.ps1) — included in repo `SaveTracker/test_ipc.ps1` (example):
```powershell
$ping = Send-IpcCommand '{"cmd":"Ping"}'
Write-Host $ping
```

- Minimal C# client example (Named Pipe):
```csharp
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

var pipeName = "SaveTracker_Command_Pipe";
using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
client.Connect(2000);
var request = JsonSerializer.Serialize(new { cmd = "getgamelist" });
var bytes = Encoding.UTF8.GetBytes(request + "\n");
client.Write(bytes, 0, bytes.Length);
client.Flush();

var reader = new StreamReader(client, Encoding.UTF8);
var resp = reader.ReadLine();
Console.WriteLine(resp);
```

- Add game example (JSON body for `addgame`):
```json
{
  "cmd": "addgame",
  "params": {
    "name": "My Game",
    "installDirectory": "C:\\Games\\MyGame",
    "executablePath": "C:\\Games\\MyGame\\game.exe",
    "steamAppId": "123456",
    "activeProfileId": "default"
  }
}
```


**Error handling and timeouts**
- The server sets a default client timeout of 30s. Long-running commands may time out from the client side.
- Large responses are guarded; if a response exceeds the configured max buffer (16KB), the server will return `{ ok: false, error: "Response too large" }`.


**Notes for integrators**
- Use lowercase or any casing for `cmd` — the server normalizes to lowercase.
- When passing typed params (numbers, booleans), ensure JSON types match expected types (e.g., `provider` is numeric enum value).
- For complex objects (like `Game`), serialize in the same shape SaveTracker uses — studs are available in `SaveTracker.Core.Resources.LOGIC.IPC.IpcJsonContext` for source-generated serializers.
- GUI-only commands will be no-ops in headless mode and produce informational logs.


If you want, I can:
- Add quick cURL-like helper scripts for Linux named pipes (or cross-platform examples),
- Generate a compact reference table (CSV/Markdown) of commands with exact param names and response DTO names,
- Add sample Playnite plugin snippet that sends commands.


---
Generated from repository source code: `SaveTracker.Core` IPC source files (`Resources/LOGIC/IPC/*`).
