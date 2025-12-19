using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.Logic.RecloneManagement;
using SaveTracker.Resources.Logic;
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
            var currentManifest = await LoadOrBuildManifest(game, currentProfile);
            var targetManifest = await LoadManifest(game, targetProfile);

            // 3. Pre-flight validation: ensure we can perform the switch
            List<string> backupPaths = new List<string>();
            foreach (var file in currentManifest.Files)
            {
                string fullBackupPath = Path.Combine(game.InstallDirectory, file.BackupPath);
                string backupDir = Path.GetDirectoryName(fullBackupPath);
                
                if (!string.IsNullOrEmpty(backupDir) && !Directory.Exists(backupDir))
                {
                    try
                    {
                        Directory.CreateDirectory(backupDir);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException($"Cannot create backup directory {backupDir}: {ex.Message}", ex);
                    }
                }
                backupPaths.Add(fullBackupPath);
            }

            // 4. Deactivate Current (Move Active -> Backup)
            foreach (var file in currentManifest.Files.ToList())
            {
                string fullActivePath = Path.Combine(game.InstallDirectory, file.OriginalPath);
                string fullBackupPath = Path.Combine(game.InstallDirectory, file.BackupPath);

                if (File.Exists(fullActivePath))
                {
                    // SAFETY: Do not manage system files
                    if (IsSystemFile(file.OriginalPath))
                    {
                        DebugConsole.WriteWarning($"Skipping system file during deactivation: {file.OriginalPath}");
                        continue;
                    }

                    try
                    {
                        // Ensure backup directory exists (if nested)
                        string backupDir = Path.GetDirectoryName(fullBackupPath);
                        if (!string.IsNullOrEmpty(backupDir) && !Directory.Exists(backupDir))
                            Directory.CreateDirectory(backupDir);

                        // Use overwrite to handle edge cases
                        File.Move(fullActivePath, fullBackupPath, overwrite: true);
                        DebugConsole.WriteDebug($"Deactivated: {file.OriginalPath}");
                        file.LastModified = DateTime.Now;
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteError($"CRITICAL: Failed to deactivate {file.OriginalPath}: {ex.Message}");
                        throw new InvalidOperationException($"Profile switch aborted. Failed to deactivate {file.OriginalPath}. Your save data is safe but profile switch incomplete.", ex);
                    }
                }
                else
                {
                    DebugConsole.WriteWarning($"File tracked in manifest not found during deactivation: {file.OriginalPath}");
                }
            }

            // Save Current Manifest immediately to record the state change
            await SaveManifest(game, currentProfile, currentManifest);

            // 5. Activate Target (Move Backup -> Active)
            if (targetManifest != null && targetManifest.Files.Count > 0)
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
                        // SAFETY: Do not manage system files
                        if (IsSystemFile(file.OriginalPath))
                        {
                            DebugConsole.WriteWarning($"Skipping system file during activation: {file.OriginalPath}");
                            continue;
                        }

                        try
                        {
                            string activeDir = Path.GetDirectoryName(fullActivePath);
                            if (!string.IsNullOrEmpty(activeDir) && !Directory.Exists(activeDir))
                                Directory.CreateDirectory(activeDir);

                            File.Move(fullBackupPath, fullActivePath, overwrite: true);
                            DebugConsole.WriteDebug($"Activated: {file.OriginalPath}");
                        }
                        catch (Exception ex)
                        {
                            DebugConsole.WriteError($"Failed to restore {file.OriginalPath}: {ex.Message}");
                            // Don't throw here—continue best effort to restore other files
                        }
                    }
                    else
                    {
                        DebugConsole.WriteWarning($"Backup file missing for target profile: {file.BackupPath}");
                    }
                }
            }
            else
            {
                DebugConsole.WriteInfo("Target profile is new/empty. Starting fresh.");
            }

            // 6. Update Game Config
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
            // Store in GameDir/.ST_PROFILES using only ProfileId for consistent lookups
            string profileDir = Path.Combine(game.InstallDirectory, ".ST_PROFILES");
            if (!Directory.Exists(profileDir))
            {
                Directory.CreateDirectory(profileDir);
                File.SetAttributes(profileDir, File.GetAttributes(profileDir) | FileAttributes.Hidden);
            }
            // Use only ProfileId to avoid name-change issues
            return Path.Combine(profileDir, $"profile_{profile.Id}.json");
        }

        private async Task<ProfileManifest> LoadManifest(Game game, Profile profile)
        {
            string path = GetManifestPath(game, profile);
            if (!File.Exists(path)) return new ProfileManifest { ProfileId = profile.Id };

            try
            {
                string json = await File.ReadAllTextAsync(path);
                return JsonSerializer.Deserialize<ProfileManifest>(json, JsonHelper.DefaultCaseInsensitive) ?? new ProfileManifest { ProfileId = profile.Id };
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
        /// For new profiles: builds from global checksums OR current filesystem state.
        /// For existing profiles: loads from manifest + reconciles with profile-specific data.
        /// </summary>
        private async Task<ProfileManifest> LoadOrBuildManifest(Game game, Profile profile)
        {
            ProfileManifest manifest;

            if (File.Exists(GetManifestPath(game, profile)))
            {
                manifest = await LoadManifest(game, profile);
            }
            else
            {
                DebugConsole.WriteInfo($"Building initial manifest for profile {profile.Name}...");
                manifest = new ProfileManifest { ProfileId = profile.Id, LastActive = DateTime.Now };

                // For DEFAULT profile: get files from global checksum data (legacy compatibility)
                // For NEW profiles: only track files that currently exist on disk
                if (profile.IsDefault)
                {
                    // DEFAULT PROFILE: Use global checksums as source of truth for legacy data
                    try
                    {
                        var checksumService = new ChecksumService();
                        var checksumData = await checksumService.LoadChecksumData(game.InstallDirectory);
                        var gameData = await ConfigManagement.GetGameData(game);

                        if (checksumData != null && checksumData.Files != null && checksumData.Files.Count > 0)
                        {
                            DebugConsole.WriteInfo($"Initializing DEFAULT profile with {checksumData.Files.Count} files from global checksums");
                            
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
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteWarning($"Failed to load default profile from global checksums: {ex.Message}");
                    }
                }
                else
                {
                    // NEW PROFILE: Start empty (user will add files when switching to it)
                    DebugConsole.WriteInfo($"New profile {profile.Name} created - it will be empty until files are managed");
                }
            }

            // RECOVERY: If manifest is empty for non-default profile, scan for deactivated files on disk
            if (manifest.Files.Count == 0 && profile.IsDefault == false)
            {
                DebugConsole.WriteInfo($"Manifest empty for non-default profile {profile.Name}. Scanning for deactivated files...");
                string suffix = ".ST_PROFILE." + SanitizeProfileName(profile.Name);

                try
                {
                    var files = Directory.GetFiles(game.InstallDirectory, "*" + suffix, SearchOption.AllDirectories);
                    if (files.Length > 0)
                    {
                        DebugConsole.WriteInfo($"Found {files.Length} deactivated files for {profile.Name}");
                        foreach (var file in files)
                        {
                            string relPath = Path.GetRelativePath(game.InstallDirectory, file);
                            string originalRelPath = relPath.Substring(0, relPath.Length - suffix.Length);

                            DebugConsole.WriteInfo($"Recovered tracked file: {originalRelPath}");
                            manifest.Files.Add(new ManagedFile
                            {
                                OriginalPath = originalRelPath,
                                BackupPath = relPath,
                                LastModified = File.GetLastWriteTime(file)
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteWarning($"Error scanning for deactivated files: {ex.Message}");
                }
            }

            // RECOVERY PHASE: For DEFAULT profile, cross-reference with global checksums to find truly "lost" files
            if (profile.IsDefault)
            {
                try
                {
                    var checksumService = new ChecksumService();
                    var checksumData = await checksumService.LoadChecksumData(game.InstallDirectory);
                    var gameData = await ConfigManagement.GetGameData(game);

                    if (checksumData?.Files != null && checksumData.Files.Count > 0)
                    {
                        foreach (var contractPath in checksumData.Files.Keys)
                        {
                            string fullPath = PathContractor.ExpandPath(contractPath, game.InstallDirectory, gameData?.DetectedPrefix);
                            string relPath = Path.GetRelativePath(game.InstallDirectory, fullPath);

                            if (IsSystemFile(relPath)) continue;

                            // Check if this file is already in our manifest
                            if (!manifest.Files.Any(f => f.OriginalPath.Equals(relPath, StringComparison.OrdinalIgnoreCase)))
                            {
                                // Check if file exists in active location or backup location
                                if (File.Exists(fullPath))
                                {
                                    // File exists in active location but NOT in manifest - add it
                                    string suffix = ".ST_PROFILE." + SanitizeProfileName(profile.Name);
                                    string backupPath = relPath + suffix;

                                    DebugConsole.WriteInfo($"[Self-Heal] Recovered missing manifest entry from active location: {relPath}");
                                    manifest.Files.Add(new ManagedFile
                                    {
                                        OriginalPath = relPath,
                                        BackupPath = backupPath,
                                        LastModified = File.GetLastWriteTime(fullPath)
                                    });
                                }
                                else
                                {
                                    // File doesn't exist in active location - check if it's in backup state
                                    string suffix = ".ST_PROFILE." + SanitizeProfileName(profile.Name);
                                    string backupPath = relPath + suffix;
                                    string fullBackupPath = Path.Combine(game.InstallDirectory, backupPath);

                                    if (File.Exists(fullBackupPath))
                                    {
                                        DebugConsole.WriteInfo($"[Self-Heal] Recovered missing manifest entry from backup: {relPath}");
                                        manifest.Files.Add(new ManagedFile
                                        {
                                            OriginalPath = relPath,
                                            BackupPath = backupPath,
                                            LastModified = File.GetLastWriteTime(fullBackupPath)
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteWarning($"[Self-Heal] Failed manifest-checksum reconciliation for default profile: {ex.Message}");
                }
            }

            return manifest;
        }

        /// <summary>
        /// Migrates global checksum data to profile-specific data.
        /// Call this once per profile to transition from the old global .savetracker file to profile-specific tracking.
        /// </summary>
        public async Task MigrateGlobalChecksumsToProfile(Game game, Profile targetProfile)
        {
            try
            {
                var checksumService = new ChecksumService();
                var globalChecksumData = await checksumService.LoadChecksumData(game.InstallDirectory);
                
                if (globalChecksumData?.Files == null || globalChecksumData.Files.Count == 0)
                {
                    DebugConsole.WriteInfo($"No global checksums to migrate for profile {targetProfile.Name}");
                    return;
                }

                DebugConsole.WriteInfo($"Migrating {globalChecksumData.Files.Count} global checksums to profile {targetProfile.Name}");

                // The global checksum data should now be considered as belonging to this profile
                // When switching away from this profile, the files will be deactivated
                // When switching to this profile, they will be reactivated
                
                // The actual migration happens implicitly:
                // 1. Files are currently active on disk
                // 2. LoadOrBuildManifest will read them from global checksums
                // 3. When switching to another profile, they get backed up with profile suffix
                // 4. When switching back, they are restored from backup

                DebugConsole.WriteSuccess($"Profile {targetProfile.Name} will now manage {globalChecksumData.Files.Count} files");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, $"Failed to migrate global checksums to profile {targetProfile.Name}");
            }
        }

        /// <summary>
        /// Consolidates all tracked files into a single profile's manifest.
        /// Useful for cleanup or when removing global checksum dependency.
        /// </summary>
        public async Task ConsolidateToProfile(Game game, Profile consolidateIntoProfile)
        {
            try
            {
                var checksumService = new ChecksumService();
                var globalChecksumData = await checksumService.LoadChecksumData(game.InstallDirectory);
                var gameData = await ConfigManagement.GetGameData(game);

                if (globalChecksumData?.Files == null || globalChecksumData.Files.Count == 0)
                {
                    DebugConsole.WriteInfo("No files to consolidate");
                    return;
                }

                var manifest = await LoadManifest(game, consolidateIntoProfile) ?? new ProfileManifest { ProfileId = consolidateIntoProfile.Id };

                DebugConsole.WriteInfo($"Consolidating {globalChecksumData.Files.Count} files into profile {consolidateIntoProfile.Name}");

                foreach (var contractPath in globalChecksumData.Files.Keys)
                {
                    string fullPath = PathContractor.ExpandPath(contractPath, game.InstallDirectory, gameData?.DetectedPrefix);
                    string relPath = Path.GetRelativePath(game.InstallDirectory, fullPath);

                    // Skip if already in manifest
                    if (manifest.Files.Any(f => f.OriginalPath.Equals(relPath, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    if (File.Exists(fullPath))
                    {
                        string backupSuffix = ".ST_PROFILE." + SanitizeProfileName(consolidateIntoProfile.Name);
                        manifest.Files.Add(new ManagedFile
                        {
                            OriginalPath = relPath,
                            BackupPath = relPath + backupSuffix,
                            LastModified = File.GetLastWriteTime(fullPath)
                        });
                    }
                }

                await SaveManifest(game, consolidateIntoProfile, manifest);
                DebugConsole.WriteSuccess($"Consolidated {manifest.Files.Count} files into {consolidateIntoProfile.Name} profile");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, $"Failed to consolidate files into profile {consolidateIntoProfile.Name}");
            }
        }

        private bool IsSystemFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            string fileName = Path.GetFileName(path);

            // Internal metadata files ONLY
            if (fileName.Equals(SaveFileUploadManager.ChecksumFilename, StringComparison.OrdinalIgnoreCase)) return true;
            if (path.Contains(".ST_PROFILES", StringComparison.OrdinalIgnoreCase)) return true;
            if (path.Contains(".savetracker", StringComparison.OrdinalIgnoreCase)) return true;

            // DO NOT filter DLLs/EXEs indiscriminately—they may be part of game saves
            // (e.g., packed executables, mod DLLs that are save state)

            return false;
        }

        private string SanitizeProfileName(string name)
        {
            return string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        }
    }
}
