using System;
using System.Collections.Generic;
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
            var config = await ConfigManagement.LoadConfigAsync();
            var targetProfile = config.Profiles.FirstOrDefault(p => p.Id == targetProfileId);

            // Handle "Default" implicit profile if null/empty ID passed (though typically we pass an ID)
            if (targetProfile == null)
            {
                // Try to resolve "Default"
                targetProfile = config.Profiles.FirstOrDefault(p => p.IsDefault)
                                ?? new Profile { Name = "Default", IsDefault = true };
            }

            string currentProfileId = game.ActiveProfileId;
            var currentProfile = config.Profiles.FirstOrDefault(p => p.Id == currentProfileId)
                                 ?? config.Profiles.FirstOrDefault(p => p.IsDefault)
                                 ?? new Profile { Name = "Default", IsDefault = true, Id = "DEFAULT_GENERATED" };

            // If we are already on this profile, do nothing?
            // Careful: currentProfileId might be null (default), so check Name or ID.
            if (currentProfile.Id == targetProfile.Id)
            {
                DebugConsole.WriteInfo($"Already on profile {targetProfile.Name}. No switch needed.");
                return;
            }

            DebugConsole.WriteInfo($"Switching Profile for {game.Name}: {currentProfile.Name} -> {targetProfile.Name}");

            // 1. Identify Tracked Files (What to hide)
            var checksumData = await _checksumService.LoadChecksumData(game.InstallDirectory);
            if (checksumData == null || checksumData.Files.Count == 0)
            {
                DebugConsole.WriteWarning("No tracked files found for this game. Switching profile will only affect new files.");
            }

            // 2. Deactivate Current Profile (Rename Active -> Suffix)
            // We only rename files that are currently "Active" (i.e. match the tracked paths)
            // AND we only rename them if they exist.

            string suffixMarker = ".ST_PROFILE.";
            string currentSuffix = suffixMarker + SanitizeProfileName(currentProfile.Name);
            string targetSuffix = suffixMarker + SanitizeProfileName(targetProfile.Name);

            // Optimization: Load game data for prefix expansion if needed
            var gameData = await ConfigManagement.GetGameData(game);
            string? detectedPrefix = gameData?.DetectedPrefix;

            if (checksumData != null && checksumData.Files != null)
            {
                foreach (var contractPath in checksumData.Files.Keys)
                {
                    string fullPath = PathContractor.ExpandPath(contractPath, game.InstallDirectory, detectedPrefix);

                    if (File.Exists(fullPath))
                    {
                        string itemsSuffix = currentSuffix;
                        // Move it
                        string destPath = fullPath + itemsSuffix;

                        try
                        {
                            if (File.Exists(destPath)) File.Delete(destPath); // Safety override? Or error? Overwrite implies we lost the old "active" state of that profile? 
                                                                              // Actually, if destPath exists, it means we have a collision. 
                                                                              // This implies the file was already "backed up". 
                                                                              // Changing the file now means we have a NEW version. We should overwrite the backup.

                            File.Move(fullPath, destPath, overwrite: true);
                            DebugConsole.WriteDebug($"Deactivated file: {Path.GetFileName(fullPath)} -> {Path.GetFileName(destPath)}");
                        }
                        catch (Exception ex)
                        {
                            DebugConsole.WriteError($"Failed to deactivate file {fullPath}: {ex.Message}");
                        }
                    }
                }
            }

            // 3. Activate Target Profile (Rename Suffix -> Active)
            // Scan for files with target suffix in the game directory (Recursive)
            // We use Directory Scan because we might have files in the "Backup" that are NOT in the current "Checksums" if they are unique to that profile?
            // Actually, YES. Brother might have `save_brother_only.sav` which I don't have.
            // So we must scan for ALL files matching the pattern.

            try
            {
                var filesToRestore = Directory.GetFiles(game.InstallDirectory, $"*{targetSuffix}", SearchOption.AllDirectories);

                foreach (var file in filesToRestore)
                {
                    // Restore path = Remove suffix
                    string originalPath = file.Substring(0, file.Length - targetSuffix.Length);

                    try
                    {
                        if (File.Exists(originalPath))
                        {
                            // Collision! This shouldn't happen if we correctly deactivated everything.
                            // But it might happen if "Brother" has a file that "Default" DOES NOT HAVE, so it wasn't deactivated.
                            // In that case, we should... backup the blocking file?
                            // If "Default" has a file "random.txt" that isn't tracked, and Brother has "random.txt.ST_PROFILE.Brother"...
                            // We should probably backup "random.txt" to ".ST_PROFILE.Default" just in case.

                            DebugConsole.WriteWarning($"Collision during restore: {originalPath} exists but was not deactivated. Force backing up to {currentSuffix}");
                            string emergencyBackup = originalPath + currentSuffix;
                            File.Move(originalPath, emergencyBackup, overwrite: true);
                        }

                        File.Move(file, originalPath);
                        DebugConsole.WriteDebug($"Activated file: {Path.GetFileName(file)} -> {Path.GetFileName(originalPath)}");
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteError($"Failed to activate file {file}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"Error scanning for target profile files: {ex.Message}");
            }

            // 4. Update Game Config
            game.ActiveProfileId = targetProfile.Id;
            await ConfigManagement.SaveGameAsync(game);

            DebugConsole.WriteSuccess($"Successfully switched {game.Name} to profile: {targetProfile.Name}");
        }

        private string SanitizeProfileName(string name)
        {
            return string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        }
    }
}
