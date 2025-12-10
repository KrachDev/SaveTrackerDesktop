using SaveTracker.Resources.HELPERS;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SaveTracker.Resources.LOGIC
{
    /// <summary>
    /// Reads game data directly from Playnite's LiteDB database file (games.db)
    /// </summary>
    public class PlayniteLibraryReader
    {
        private byte[]? _lastData;

        // Binary markers for fields (from Python script)
        private static readonly byte[] IdMarker = Encoding.ASCII.GetBytes("_id\x00");
        private static readonly byte[] NameMarker = Encoding.ASCII.GetBytes("Name\x00");
        private static readonly byte[] GameIdMarker = Encoding.ASCII.GetBytes("GameId\x00");
        private static readonly byte[] InstallDirMarker = Encoding.ASCII.GetBytes("InstallDirectory\x00");
        private static readonly byte[] PlatformMarker = Encoding.ASCII.GetBytes("Platform");

        private static readonly HashSet<string> Blacklist = new(StringComparer.OrdinalIgnoreCase)
        {
            "youtube", "facebook", "twitter", "instagram", "discord", "twitch",
            "steam", "epic", "gog", "itch", "wiki", "wikipedia", "subreddit",
            "official website", "community wiki", "url", "played", "not played",
            "bluesky", "google play", "app store"
        };

        public PlayniteLibraryReader()
        {
        }

        public List<PlayniteGame> ReadGamesFromDb(string localDbPath)
        {
            try
            {
                byte[]? data = ReadFile(localDbPath);
                if (data == null)
                {
                    DebugConsole.WriteError("Failed to read Playnite database file.");
                    return new List<PlayniteGame>();
                }

                return Parse(data);
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Error reading games from Playnite DB");
                return new List<PlayniteGame>();
            }
        }

        private byte[]? ReadFile(string filename)
        {
            // Method 1: Standard read
            try
            {
                // Use FileShare.ReadWrite to attempt reading even if Playnite has it open
                using var fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var ms = new MemoryStream();
                fs.CopyTo(ms);
                byte[] data = ms.ToArray();
                _lastData = data;
                return data;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Standard read failed: {ex.Message}");
            }

            // Method 1.5: Copy to Temp (sometimes bypasses certain sharing violations)
            try
            {
                // DebugConsole.WriteInfo("Attempting to copy to temp..."); // Reduce verbosity
                string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".db");

                // Allow read/write share during copy
                using (var source = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var dest = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                {
                    source.CopyTo(dest);
                }

                byte[] data = File.ReadAllBytes(tempPath);
                try { File.Delete(tempPath); } catch { }

                _lastData = data;
                return data;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"Copy to temp failed: {ex.Message}");
            }

            // Method 2: Volume Shadow Copy (Windows only)
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    DebugConsole.WriteInfo("Attempting to read via Volume Shadow Copy...");
                    byte[]? data = ReadViaShadowCopy(filename);
                    if (data != null)
                    {
                        _lastData = data;
                        return data;
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteWarning($"Shadow copy read failed: {ex.Message}");
                }
            }

            // Method 3: Use cached data if available
            if (_lastData != null)
            {
                DebugConsole.WriteWarning("Using cached data from previous read.");
                return _lastData;
            }

            return null;
        }

        private byte[]? ReadViaShadowCopy(string filename)
        {
            if (!IsRunAsAdmin())
            {
                DebugConsole.WriteWarning("Skipping VSS fallback because application is not running as Administrator.");
                return null;
            }

            // Strategy A: esentutl (Simplest system tool for VSS copy)
            DebugConsole.WriteInfo("Attempting read via esentutl...");
            byte[]? data = ReadViaEsentutl(filename);
            if (data != null) return data;

            // Strategy B: vssadmin (Often fails on Client OS)
            data = ReadViaVssAdmin(filename);
            if (data != null) return data;

            // Strategy C: PowerShell WMI (Robust fallback)
            DebugConsole.WriteInfo("vssadmin failed/unsupported, attempting PowerShell WMI fallback...");
            return ReadViaPowerShell(filename);
        }

        private byte[]? ReadViaEsentutl(string filename)
        {
            // esentutl /y <source> /d <dest> /o
            try
            {
                string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".db");

                var startInfo = new ProcessStartInfo
                {
                    FileName = "esentutl",
                    Arguments = $"/y \"{filename}\" /d \"{tempPath}\" /o",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                DebugConsole.WriteInfo($"Running esentutl: {startInfo.Arguments}");

                using var process = Process.Start(startInfo);
                if (process == null) return null;

                // esentutl is noisy, we only care if it succeeds
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    DebugConsole.WriteWarning($"esentutl failed with exit code {process.ExitCode}");
                    return null;
                }

                if (File.Exists(tempPath))
                {
                    try
                    {
                        return File.ReadAllBytes(tempPath);
                    }
                    finally
                    {
                        File.Delete(tempPath);
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"esentutl strategy failed: {ex.Message}");
                return null;
            }
        }

        private byte[]? ReadViaVssAdmin(string filename)
        {
            try
            {
                string drive = Path.GetPathRoot(filename) ?? "C:\\";
                if (!drive.EndsWith("\\")) drive += "\\";

                var startInfo = new ProcessStartInfo
                {
                    FileName = "vssadmin",
                    Arguments = $"create shadow /for={drive.TrimEnd('\\')}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return null;

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    // Don't log error here, expected on some OS versions
                    return null;
                }

                string? shadowPath = null;
                foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.Contains("Shadow Copy Volume Name:"))
                    {
                        shadowPath = line.Split(':')[1].Trim();
                        break;
                    }
                }

                if (string.IsNullOrEmpty(shadowPath)) return null;

                try
                {
                    return ReadFromShadowPath(shadowPath, filename, drive);
                }
                finally
                {
                    // Cleanup
                    string? shadowId = null;
                    foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (line.Contains("Shadow Copy ID:")) shadowId = line.Split(':')[1].Trim();
                    }

                    if (!string.IsNullOrEmpty(shadowId))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "vssadmin",
                            Arguments = $"delete shadows /shadow={shadowId} /quiet",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        })?.WaitForExit();
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private byte[]? ReadViaPowerShell(string filename)
        {
            try
            {
                string drive = Path.GetPathRoot(filename) ?? "C:\\";
                if (!drive.EndsWith("\\")) drive += "\\"; // C:\

                // PowerShell script to create shadow copy using CIM/WMI
                // Uses "ClientAccessible" context which is valid for client OS
                string psScript = $@"
$s = (Get-WmiObject -List Win32_ShadowCopy).Create('ClientAccessible', '{drive}');
if ($s.ReturnValue -eq 0) {{
    Write-Output ""ShadowID=$($s.ShadowID)""
    $shadow = Get-WmiObject Win32_ShadowCopy | Where-Object {{ $_.ID -eq $s.ShadowID }}
    Write-Output ""DeviceObject=$($shadow.DeviceObject)""
}} else {{
    Exit 1
}}
";
                // Encode to Base64 to avoid escaping hell
                string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(psScript));

                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -EncodedCommand {encoded}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                DebugConsole.WriteInfo("Exec PowerShell WMI...");

                using var process = Process.Start(startInfo);
                if (process == null) return null;

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    DebugConsole.WriteError($"PowerShell ShadowCopy failed. Exit: {process.ExitCode}. Err: {error}");
                    return null;
                }

                // Parse
                string? shadowId = null;
                string? deviceObject = null;

                foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith("ShadowID=")) shadowId = line.Substring("ShadowID=".Length).Trim();
                    if (line.StartsWith("DeviceObject=")) deviceObject = line.Substring("DeviceObject=".Length).Trim();
                }

                if (string.IsNullOrEmpty(shadowId) || string.IsNullOrEmpty(deviceObject))
                {
                    DebugConsole.WriteError("Could not parse PowerShell output.");
                    return null;
                }

                try
                {
                    return ReadFromShadowPath(deviceObject, filename, drive);
                }
                finally
                {
                    // Cleanup: wmic or powershell
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        // Generic cleanup
                        Arguments = $"-Command \"(Get-WmiObject Win32_ShadowCopy | Where-Object {{ $_.ID -eq '{shadowId}' }}).Delete()\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteWarning($"PowerShell fallback exception: {ex.Message}");
                return null;
            }
        }

        private byte[]? ReadFromShadowPath(string shadowPath, string originalFilename, string driveRoot)
        {
            // Construct new path
            // filename: C:\Users\User\file.db
            // drive: C:\
            // relative: Users\User\file.db
            string relativePath = originalFilename.Substring(driveRoot.Length);
            string shadowFile = Path.Combine(shadowPath, relativePath);

            DebugConsole.WriteInfo($"Reading from shadow path: {shadowFile}");

            using var fs = new FileStream(shadowFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var ms = new MemoryStream();
            fs.CopyTo(ms);
            return ms.ToArray();
        }

        private static bool IsRunAsAdmin()
        {
            try
            {
#if WINDOWS
                var principal = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent());
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
#else
                return false;
#endif
            }
            catch
            {
                return false;
            }
        }

        private List<PlayniteGame> Parse(byte[] data)
        {
            var games = new List<PlayniteGame>();
            int pos = 0;
            int len = data.Length;

            while (true)
            {
                // Find next game document (marked by _id)
                int idPos = FindPattern(data, IdMarker, pos);
                if (idPos == -1) break;

                // Look for Name field within next 3000 bytes
                int searchStart = idPos;
                int searchEnd = Math.Min(searchStart + 3000, len);

                int nameOffset = FindPattern(data, NameMarker, searchStart, searchEnd);

                if (nameOffset == -1)
                {
                    pos = idPos + 1;
                    continue;
                }

                // Extract game name
                // nameOffset is absolute index here from my FindPattern impl
                int namePos = nameOffset + NameMarker.Length;
                var (name, newPos) = ExtractString(data, namePos);

                // Validate name
                if (string.IsNullOrWhiteSpace(name) || name.Length < 3)
                {
                    pos = idPos + 1;
                    continue;
                }

                if (Blacklist.Contains(name) || name.Contains("http://") || name.Contains("https://"))
                {
                    pos = idPos + 1;
                    continue;
                }

                // Define document boundaries
                int nextIdPos = FindPattern(data, IdMarker, idPos + 1);
                int docEnd = (nextIdPos != -1) ? nextIdPos : Math.Min(idPos + 5000, len);

                // Extract other fields by searching within [idPos, docEnd]
                string gameId = "N/A";
                string installDir = "N/A";
                string platform = "N/A";

                int gameIdOffset = FindPattern(data, GameIdMarker, idPos, docEnd);
                if (gameIdOffset != -1)
                {
                    var (val, _) = ExtractString(data, gameIdOffset + GameIdMarker.Length);
                    if (!string.IsNullOrEmpty(val)) gameId = val;
                }

                int installDirOffset = FindPattern(data, InstallDirMarker, idPos, docEnd);
                if (installDirOffset != -1)
                {
                    var (val, _) = ExtractString(data, installDirOffset + InstallDirMarker.Length);
                    if (!string.IsNullOrEmpty(val)) installDir = val;
                }

                int platformOffset = FindPattern(data, PlatformMarker, idPos, docEnd);
                if (platformOffset != -1)
                {
                    var (val, _) = ExtractString(data, platformOffset + PlatformMarker.Length);
                    if (!string.IsNullOrEmpty(val)) platform = val;
                }

                games.Add(new PlayniteGame
                {
                    Name = name.Trim(),
                    GameId = gameId,
                    InstallDirectory = installDir,
                    IsInstalled = !string.IsNullOrEmpty(installDir) && installDir != "N/A",
                    ExecutablePath = installDir // Temporary placeholder, scanned later
                });

                pos = idPos + 1;
            }

            // Remove duplicates by name
            return games.GroupBy(g => g.Name).Select(g => g.First()).ToList();
        }

        private int FindPattern(byte[] data, byte[] pattern, int start, int end = -1)
        {
            if (end == -1) end = data.Length;
            int n = data.Length;
            int m = pattern.Length;
            int searchLen = end - m;

            for (int i = start; i <= searchLen; i++)
            {
                bool match = true;
                for (int j = 0; j < m; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        private (string?, int) ExtractString(byte[] data, int pos)
        {
            try
            {
                if (pos + 4 > data.Length) return (null, pos);

                // Read length (int32 little endian)
                int length = BitConverter.ToInt32(data, pos);

                // Sanity check length
                if (length > 2000 || length <= 0)
                {
                    return (null, pos + 4);
                }

                pos += 4;
                if (pos + length > data.Length) return (null, pos);

                string str = Encoding.UTF8.GetString(data, pos, length);

                // Cleanup string
                var sb = new StringBuilder();
                foreach (char c in str)
                {
                    if ((c >= 32 && c <= 126) || c == '\n' || c == '\r' || c == '\t')
                        sb.Append(c);
                }
                string result = sb.ToString().Trim();

                int newPos = pos + length;
                if (newPos < data.Length && data[newPos] == 0)
                    newPos++;

                return (result, newPos);
            }
            catch
            {
                return (null, pos);
            }
        }
    }

    public class PlayniteGame
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? InstallDirectory { get; set; }
        public string? ExecutablePath { get; set; }
        public string? GameId { get; set; }
        public bool IsInstalled { get; set; }

        // Helper to find exe if we only have directory
        public void TryFindExecutable()
        {
            if (string.IsNullOrEmpty(InstallDirectory) || InstallDirectory == "N/A") return;
            if (!Directory.Exists(InstallDirectory)) return;

            try
            {
                var exes = Directory.GetFiles(InstallDirectory, "*.exe", SearchOption.AllDirectories);
                var validExes = exes.Where(e =>
                {
                    string name = Path.GetFileName(e).ToLower();
                    return !name.Contains("uninstall") && !name.Contains("setup") && !name.Contains("dxwebsetup")
                           && !name.Contains("unitycrashhandler");
                }).ToList();

                if (validExes.Any())
                {
                    // Pick largest exe by file size as a heuristic for the main executable
                    ExecutablePath = validExes.OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
                }
            }
            catch { }
        }
    }
}
