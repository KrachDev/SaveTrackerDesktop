The methods have been designed but I'm having difficulty adding them to the large RcloneFileOperations.cs file using automated tools.

Here's what you need to do manually:

## Step 1: Add methods to RcloneFileOperations.cs

Open `SaveTracker/Resources/LOGIC/RecloneManagement/RcloneFileOperations.cs` and add these THREE methods after the `RemoteFileExists` method (around line 495):

```csharp
public async Task<bool> CheckCloudSaveExistsAsync(string remoteBasePath)
{
    try
    {
        var result = await _executor.ExecuteRcloneCommand(
            $"lsf \"{remoteBasePath}\" --config \"{_configPath}\" --max-depth 1",
            TimeSpan.FromSeconds(15)
        );

        return result.Success && !string.IsNullOrWhiteSpace(result.Output);
    }
    catch (Exception ex)
    {
        DebugConsole.WriteException(ex, "Failed to check cloud save existence");
        return false;
    }
}

public async Task<bool> DownloadDirectory(string remotePath, string localPath)
{
    try
    {
        DebugConsole.WriteInfo($"Downloading directory from {remotePath} to {localPath}");

        var result = await _executor.ExecuteRcloneCommand(
            $"copy \"{remotePath}\" \"{localPath}\" --config \"{_configPath}\" --progress",
            TimeSpan.FromMinutes(5)
        );

        if (result.Success)
        {
            DebugConsole.WriteSuccess("Directory download successful");
            return true;
        }
        else
        {
            DebugConsole.WriteError($"Directory download failed: {result.Error}");
            return false;
        }
    }
    catch (Exception ex)
    {
        DebugConsole.WriteException(ex, "Directory download exception");
        return false;
    }
}

public async Task<bool> DownloadWithChecksumAsync(string remotePath, Game game)
{
    string stagingFolder = Path.Combine(Path.GetTempPath(), "SaveTracker_Download_" + Guid.NewGuid().ToString("N"));
    
    try
    {
        DebugConsole.WriteInfo($"Downloading cloud saves to staging folder: {stagingFolder}");
        Directory.CreateDirectory(stagingFolder);

        var downloadResult = await _executor.ExecuteRcloneCommand(
            $"copy \"{remotePath}\" \"{stagingFolder}\" --config \"{_configPath}\" --progress",
            TimeSpan.FromMinutes(5)
        );

        if (!downloadResult.Success)
        {
            DebugConsole.WriteError($"Failed to download files to staging: {downloadResult.Error}");
            return false;
        }

        string checksumFilePath = Path.Combine(stagingFolder, ".savetracker_checksums.json");
        if (!File.Exists(checksumFilePath))
        {
            DebugConsole.WriteWarning("Checksum file not found - using fallback");
            await CopyDirectoryContents(stagingFolder, game.InstallDirectory);
            return true;
        }

        DebugConsole.WriteInfo("Reading checksum file...");
        string checksumJson = await File.ReadAllTextAsync(checksumFilePath);
        var checksumData = JsonConvert.DeserializeObject<GameUploadData>(checksumJson);

        if (checksumData?.Files == null || checksumData.Files.Count == 0)
        {
            DebugConsole.WriteWarning("Checksum file is empty or invalid");
            return false;
        }

        int successCount = 0;
        int failCount = 0;

        foreach (var fileEntry in checksumData.Files)
        {
            try
            {
                string relativePath = fileEntry.Key;
                string fileName = Path.GetFileName(relativePath);
                string sourceFile = Path.Combine(stagingFolder, fileName);
                string targetPath = fileEntry.Value.GetAbsolutePath(game.InstallDirectory);

                if (!File.Exists(sourceFile))
                {
                    DebugConsole.WriteWarning($"File not found in staging: {fileName}");
                    failCount++;
                    continue;
                }

                string? targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                File.Copy(sourceFile, targetPath, overwrite: true);
                DebugConsole.WriteSuccess($"✓ Restored: {relativePath}");
                successCount++;
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"✗ Failed to restore {fileEntry.Key}: {ex.Message}");
                failCount++;
            }
        }

        DebugConsole.WriteSuccess($"Download complete: {successCount} files restored, {failCount} failed");
        return failCount == 0;
    }
    catch (Exception ex)
    {
        DebugConsole.WriteException(ex, "Download with checksum failed");
        return false;
    }
    finally
    {
        try
        {
            if (Directory.Exists(stagingFolder))
            {
                Directory.Delete(stagingFolder, recursive: true);
                DebugConsole.WriteInfo("Staging folder cleaned up");
            }
        }
        catch (Exception ex)
        {
            DebugConsole.WriteWarning($"Failed to clean up staging folder: {ex.Message}");
        }
    }
}

private async Task CopyDirectoryContents(string sourceDir, string targetDir)
{
    foreach (string file in Directory.GetFiles(sourceDir))
    {
        string fileName = Path.GetFileName(file);
        if (fileName == ".savetracker_checksums.json")
            continue;

        string targetPath = Path.Combine(targetDir, fileName);
        File.Copy(file, targetPath, overwrite: true);
        DebugConsole.WriteInfo($"Copied: {fileName}");
    }
    await Task.CompletedTask;
}
```

## Step 2: The ViewModel change has already been applied

The change to `MainWindowViewModel.cs` line 436 has been successfully applied - it now uses `DownloadWithChecksumAsync` instead of `DownloadDirectory`.

## What this fixes

- **Upload**: Already uploads the `.savetracker_checksums.json` file ✓
- **Download**: Now downloads files to a staging folder, reads the checksum JSON to know where each file should go, and restores them to their correct locations ✓
