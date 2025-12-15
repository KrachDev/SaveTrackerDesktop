using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.Logic.RecloneManagement;
using SaveTracker.Resources.SAVE_SYSTEM;

namespace SaveTracker.Resources.LOGIC
{
    public class ProfileManager
    {
        private readonly ChecksumService _checksumService;

        public ProfileManager()
        {
            _checksumService = new ChecksumService();
        }

        public async Task<List<Profile>> GetProfilesAsync()
        {
            var config = await ConfigManagement.LoadConfigAsync();

            // Ensure Default profile exists
            if (!config.Profiles.Any(p => p.IsDefault))
            {
                config.Profiles.Insert(0, new Profile
                {
                    Name = "Default",
                    IsDefault = true,
                    Id = "DEFAULT_PROFILE_ID" // Fixed ID for consistency
                });
                await ConfigManagement.SaveConfigAsync(config);
            }

            return config.Profiles;
        }

        public async Task AddProfileAsync(string name)
        {
            var config = await ConfigManagement.LoadConfigAsync();
            if (config.Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new Exception("Profile already exists.");
            }

            var newProfile = new Profile { Name = name, IsDefault = false };
            config.Profiles.Add(newProfile);
            await ConfigManagement.SaveConfigAsync(config);
            DebugConsole.WriteSuccess($"Created new profile: {name}");
        }

        public async Task DeleteProfileAsync(string profileId)
        {
            var config = await ConfigManagement.LoadConfigAsync();
            var profile = config.Profiles.FirstOrDefault(p => p.Id == profileId);

            if (profile == null) throw new Exception("Profile not found.");
            if (profile.IsDefault) throw new Exception("Cannot delete default profile.");

            config.Profiles.Remove(profile);
            await ConfigManagement.SaveConfigAsync(config);
            DebugConsole.WriteSuccess($"Deleted profile: {profile.Name}");

            // Note: We are NOT deleting the files on disk for safety. 
            // Users can manually clean up if they want.
        }

        public async Task SwitchProfileAsync(Game game, string targetProfileId)
        {
            // 1. SAFETY CHECK: Is Game Running?
            if (IsGameRunning(game))
            {
                throw new InvalidOperationException("Cannot switch profiles while the game is running. Please close the game first.");
            }

            var config = await ConfigManagement.LoadConfigAsync();
            var targetProfile = config.Profiles.FirstOrDefault(p => p.Id == targetProfileId);

            // Handle "Default" implicit profile if null/empty ID passed
            if (targetProfile == null)
            {
                targetProfile = config.Profiles.FirstOrDefault(p => p.IsDefault)
                                ?? new Profile { Name = "Default", IsDefault = true };
            }

            string currentProfileId = game.ActiveProfileId;
            var currentProfile = config.Profiles.FirstOrDefault(p => p.Id == currentProfileId)
                                 ?? config.Profiles.FirstOrDefault(p => p.IsDefault)
                                 ?? new Profile { Name = "Default", IsDefault = true, Id = "DEFAULT_GENERATED" };

            if (currentProfile.Id == targetProfile.Id)
            {
                DebugConsole.WriteInfo($"Already on profile {targetProfile.Name}. No switch needed.");
                return;
            }

            DebugConsole.WriteInfo($"Switching Profile for {game.Name}: {currentProfile.Name} -> {targetProfile.Name}");

            var quarantine = new QuarantineManager(game.InstallDirectory);

            // 2. Load Manifests
            // If Manifest doesn't exist (Legacy/New), build it from current state for the Current Profile
            var currentManifest = await LoadOrBuildManifest(game, currentProfile);
            var targetManifest = await LoadManifest(game, targetProfile); // Target might be empty if new

            // 3. Deactivate Current (Move Active -> Backup)
            foreach (var file in currentManifest.Files.ToList()) // ToList to allow modification if needed
            {
                string fullActivePath = Path.Combine(game.InstallDirectory, file.OriginalPath);
                string fullBackupPath = Path.Combine(game.InstallDirectory, file.BackupPath);

                if (File.Exists(fullActivePath))
                {
                    // Update Hash before backup? Optional.
                    try
                    {
                        // Ensure backup directory exists (if nested)
                        string backupDir = Path.GetDirectoryName(fullBackupPath);
                        if (!string.IsNullOrEmpty(backupDir) && !Directory.Exists(backupDir))
                            Directory.CreateDirectory(backupDir);

                        File.Move(fullActivePath, fullBackupPath, overwrite: true);
                        DebugConsole.WriteDebug($"Deactivated: {file.OriginalPath}");
                        file.LastModified = DateTime.Now;
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteError($"Failed to deactivate {file.OriginalPath}: {ex.Message}");
                        // Critical failure? If we fail to move 1 file, should we stop?
                        // For now, continue best effort.
                    }
                }
                else
                {
                    DebugConsole.WriteWarning($"File tracked in manifest not found during deactivation: {file.OriginalPath}");
                }
            }

            // Save Current Manifest immediately to record the state change
            await SaveManifest(game, currentProfile, currentManifest);

            // 4. Activate Target (Move Backup -> Active)
            // If target manifest is empty (New Profile), we just start clean.
            if (targetManifest != null)
            {
                foreach (var file in targetManifest.Files)
                {
                    string fullActivePath = Path.Combine(game.InstallDirectory, file.OriginalPath);
                    string fullBackupPath = Path.Combine(game.InstallDirectory, file.BackupPath);

                    // Safety: Check if Active Path is blocked by an ORPHAN
                    if (File.Exists(fullActivePath))
                    {
                        quarantine.QuarantineFile(fullActivePath, $"Blocking restoration of {targetProfile.Name}'s {file.OriginalPath}");
                    }

                    if (File.Exists(fullBackupPath))
                    {
                        try
                        {
                            string activeDir = Path.GetDirectoryName(fullActivePath);
                            if (!string.IsNullOrEmpty(activeDir) && !Directory.Exists(activeDir))
                                Directory.CreateDirectory(activeDir);

                            File.Move(fullBackupPath, fullActivePath);
                            DebugConsole.WriteDebug($"Activated: {file.OriginalPath}");
                        }
                        catch (Exception ex)
                        {
                            DebugConsole.WriteError($"Failed to restore {file.OriginalPath}: {ex.Message}");
                        }
                    }
                    else
                    {
                        DebugConsole.WriteWarning($"Backup file missing for target profile: {file.BackupPath}");
                    }
                }
            }

            // 5. Update Game Config
            game.ActiveProfileId = targetProfile.Id;
            await ConfigManagement.SaveGameAsync(game);

            DebugConsole.WriteSuccess($"Successfully switched {game.Name} to profile: {targetProfile.Name}");
        }

        private bool IsGameRunning(Game game)
        {
            try
            {
                if (string.IsNullOrEmpty(game.ExecutablePath)) return false;
                string exeName = Path.GetFileNameWithoutExtension(game.ExecutablePath);
                var processes = System.Diagnostics.Process.GetProcessesByName(exeName);
                return processes.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        // Helpers for Manifests
        private string GetManifestPath(Game game, Profile profile)
        {
            // Manifests stored in game directory? Or helper directory?
            // Safer to store in AppData/Manifests to avoid cluttering game dir? 
            // Or store in GameDir hidden. 
            // Decision: Store in GameDir/.ST_PROFILES/profile_{id}.manifest
            string profileDir = Path.Combine(game.InstallDirectory, ".ST_PROFILES");
            if (!Directory.Exists(profileDir))
            {
                Directory.CreateDirectory(profileDir);
                File.SetAttributes(profileDir, File.GetAttributes(profileDir) | FileAttributes.Hidden);
            }
            return Path.Combine(profileDir, $"profile_{SanitizeProfileName(profile.Name)}_{profile.Id}.json");
        }

        private async Task<ProfileManifest> LoadManifest(Game game, Profile profile)
        {
            string path = GetManifestPath(game, profile);
            if (!File.Exists(path)) return new ProfileManifest { ProfileId = profile.Id };

            try
            {
                string json = await File.ReadAllTextAsync(path);
                return JsonSerializer.Deserialize<ProfileManifest>(json) ?? new ProfileManifest { ProfileId = profile.Id };
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"Failed to load manifest for {profile.Name}: {ex.Message}");
                return new ProfileManifest { ProfileId = profile.Id };
            }
        }

        private async Task SaveManifest(Game game, Profile profile, ProfileManifest manifest)
        {
            string path = GetManifestPath(game, profile);
            string json = JsonSerializer.Serialize(manifest, JsonHelper.DefaultIndented);
            await File.WriteAllTextAsync(path, json);
        }

        /// <summary>
        /// Loads existing manifest OR (if missing) builds one from the *current* state of the filesystem.
        /// This bridges the gap between the old system and the new one.
        /// </summary>
        private async Task<ProfileManifest> LoadOrBuildManifest(Game game, Profile profile)
        {
            if (File.Exists(GetManifestPath(game, profile)))
            {
                return await LoadManifest(game, profile);
            }

            DebugConsole.WriteInfo($"Building initial manifest for profile {profile.Name}...");
            var manifest = new ProfileManifest { ProfileId = profile.Id, LastActive = DateTime.Now };

            // Get currently tracked files from ChecksumService (the old "source of truth")
            var checksumData = await _checksumService.LoadChecksumData(game.InstallDirectory);
            var gameData = await ConfigManagement.GetGameData(game);

            if (checksumData != null && checksumData.Files != null)
            {
                foreach (var contractPath in checksumData.Files.Keys)
                {
                    string fullPath = PathContractor.ExpandPath(contractPath, game.InstallDirectory, gameData?.DetectedPrefix);

                    // We only add it to the manifest if it currently exists
                    if (File.Exists(fullPath))
                    {
                        string relPath = Path.GetRelativePath(game.InstallDirectory, fullPath);
                        string backupSuffix = ".ST_PROFILE." + SanitizeProfileName(profile.Name);

                        manifest.Files.Add(new ManagedFile
                        {
                            OriginalPath = relPath,
                            BackupPath = relPath + backupSuffix,
                            LastModified = File.GetLastWriteTime(fullPath)
                        });
                    }
                }
            }

            return manifest;
        }

        private string SanitizeProfileName(string name)
        {
            return string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        }
    }
}
