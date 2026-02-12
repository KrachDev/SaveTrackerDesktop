using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.Logic.RecloneManagement;
using SaveTracker.Resources.SAVE_SYSTEM;

namespace SaveTracker.Resources.Logic.Testing
{
    public class SaveArchiverTestChamber
    {
        public static async Task RunTest()
        {
            DebugConsole.WriteInfo("=== SaveArchiver Test Chamber Starting ===");

            string testDir = Path.Combine(Path.GetTempPath(), "SaveTracker_TestChamber_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                // 1. Setup Test Files
                string gameDir = Path.Combine(testDir, "MockGame");
                Directory.CreateDirectory(gameDir);

                var testFiles = new List<string>();
                for (int i = 1; i <= 5; i++)
                {
                    string filePath = Path.Combine(gameDir, $"save_{i}.dat");
                    await File.WriteAllTextAsync(filePath, $"Content of save file {i} with some extra padding to make it a bit larger than a few bytes.");
                    testFiles.Add(filePath);
                }

                var metadata = new GameUploadData
                {
                    PlayTime = TimeSpan.FromHours(15.5),
                    LastUpdated = DateTime.UtcNow,
                    Files = new Dictionary<string, FileChecksumRecord>
                    {
                        { "save_1.dat", new FileChecksumRecord { Checksum = "abc", FileSize = 100 } }
                    }
                };

                string archivePath = Path.Combine(testDir, "TestData.sta");
                var archiver = new SaveArchiver();

                // 2. Test Packing
                DebugConsole.WriteInfo("Action: Packing files...");
                var packResult = await archiver.PackAsync(archivePath, testFiles, gameDir, metadata);

                if (packResult.Success)
                    DebugConsole.WriteSuccess($"Pack Success: {Path.GetFileName(archivePath)} ({new FileInfo(archivePath).Length} bytes)");
                else
                    throw new Exception("Pack failed: " + packResult.Error);

                // 3. Test Peeking
                DebugConsole.WriteInfo("Action: Peeking Metadata (First 64KB simulation)...");
                using (var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read))
                {
                    var peeked = await archiver.PeekMetadataAsync(fs);
                    if (peeked != null && peeked.PlayTime == metadata.PlayTime)
                        DebugConsole.WriteSuccess($"Peek Success: PlayTime {peeked.PlayTime} recovered correctly.");
                    else
                        throw new Exception("Peek failed or data mismatch.");
                }

                // 4. Test Unpacking
                DebugConsole.WriteInfo("Action: Unpacking to fresh directory...");
                string unpackDir = Path.Combine(testDir, "Unpacked");
                Directory.CreateDirectory(unpackDir);

                var unpackResult = await archiver.UnpackAsync(archivePath, unpackDir, unpackDir);

                if (unpackResult.Success)
                {
                    DebugConsole.WriteSuccess("Unpack Success: Metadata recovered.");
                    // Verify files
                    bool allExist = testFiles.All(f => File.Exists(Path.Combine(unpackDir, Path.GetFileName(f))));
                    if (allExist)
                        DebugConsole.WriteSuccess("File Verification: All files correctly extracted.");
                    else
                        throw new Exception("Unpack verification failed: Missing files.");
                }
                else
                    throw new Exception("Unpack failed: " + unpackResult.Error);

                DebugConsole.WriteSuccess("=== Test Chamber Passed Successfully ===");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError("!!! Test Chamber Failed !!!");
                DebugConsole.WriteException(ex, "Chamber Failure Detail");
            }
            finally
            {
                // Cleanup
                try { Directory.Delete(testDir, true); } catch { }
            }
        }
    }
}
