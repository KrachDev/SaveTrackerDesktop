using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.SAVE_SYSTEM;

namespace SaveTracker.Resources.Logic.RecloneManagement
{
    /// <summary>
    /// Handles packing and unpacking of .sta (SaveTracker Archive) files.
    /// Format: [128B Header] [Variable Metadata] [Zip Payload]
    /// </summary>
    public class SaveArchiver
    {
        private const string MagicBytes = "STARCH";
        private const ushort CurrentVersion = 1;
        private const int HeaderSize = 128;

        public class PackResult
        {
            public bool Success { get; set; }
            public string? ArchivePath { get; set; }
            public long MetadataSize { get; set; }
            public string? Error { get; set; }
        }

        public class UnpackResult
        {
            public bool Success { get; set; }
            public GameUploadData? Metadata { get; set; }
            public string? Error { get; set; }
        }

        /// <summary>
        /// Packs files and metadata into a .sta archive
        /// </summary>
        public async Task<PackResult> PackAsync(
            string outputPath,
            List<string> filePaths,
            string gameDirectory,
            GameUploadData metadata,
            string? detectedPrefix = null)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // 1. Serialize Metadata
                    string jsonMetadata = JsonConvert.SerializeObject(metadata, Formatting.None);
                    byte[] metadataBytes = Encoding.UTF8.GetBytes(jsonMetadata);

                    using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var writer = new BinaryWriter(fs))
                    {
                        // 2. Write Header (128 bytes)
                        writer.Write(Encoding.ASCII.GetBytes(MagicBytes)); // 6 bytes
                        writer.Write(CurrentVersion);                     // 2 bytes
                        writer.Write((long)metadataBytes.Length);        // 8 bytes

                        // Pad header to 128 bytes
                        byte[] padding = new byte[HeaderSize - (int)fs.Position];
                        writer.Write(padding);

                        // 3. Write Metadata
                        writer.Write(metadataBytes);

                        // 4. Write Zip Payload
                        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: true))
                        {
                            foreach (var filePath in filePaths)
                            {
                                if (!File.Exists(filePath)) continue;

                                // Contract path for the archive
                                string relativePath = PathContractor.ContractPath(filePath, gameDirectory, detectedPrefix);
                                // Ensure forward slashes for zip compatibility
                                string entryName = relativePath.Replace('\\', '/');

                                archive.CreateEntryFromFile(filePath, entryName, CompressionLevel.Optimal);
                            }
                        }
                    }

                    return new PackResult { Success = true, ArchivePath = outputPath, MetadataSize = metadataBytes.Length };
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteException(ex, "Failed to pack .sta archive");
                    return new PackResult { Success = false, Error = ex.Message };
                }
            });
        }

        /// <summary>
        /// Extracts metadata from the beginning of a stream (the "Peek")
        /// </summary>
        public async Task<GameUploadData?> PeekMetadataAsync(Stream stream)
        {
            try
            {
                byte[] headerBuffer = new byte[HeaderSize];
                int read = await stream.ReadAsync(headerBuffer, 0, HeaderSize);
                if (read < HeaderSize) return null;

                using (var ms = new MemoryStream(headerBuffer))
                using (var reader = new BinaryReader(ms))
                {
                    string magic = Encoding.ASCII.GetString(reader.ReadBytes(6));
                    if (magic != MagicBytes)
                    {
                        DebugConsole.WriteWarning("Invalid .sta magic bytes");
                        return null;
                    }

                    ushort version = reader.ReadUInt16();
                    long metadataSize = reader.ReadInt64();

                    // Read metadata
                    byte[] metadataBuffer = new byte[metadataSize];
                    read = await stream.ReadAsync(metadataBuffer, 0, (int)metadataSize);
                    if (read < metadataSize) return null;

                    string json = Encoding.UTF8.GetString(metadataBuffer);
                    return JsonConvert.DeserializeObject<GameUploadData>(json);
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to peek metadata from archive");
                return null;
            }
        }

        /// <summary>
        /// Unpacks a .sta archive to a directory
        /// </summary>
        public async Task<UnpackResult> UnpackAsync(string archivePath, string destinationDirectory, string? gameDirectory = null)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        // 1. Read Header
                        byte[] headerBuffer = new byte[HeaderSize];
                        fs.Read(headerBuffer, 0, HeaderSize);

                        using (var ms = new MemoryStream(headerBuffer))
                        using (var reader = new BinaryReader(ms))
                        {
                            string magic = Encoding.ASCII.GetString(reader.ReadBytes(6));
                            if (magic != MagicBytes) throw new Exception("Invalid STARCH magic");

                            ushort version = reader.ReadUInt16();
                            long metadataSize = reader.ReadInt64();

                            // 2. Read Metadata
                            byte[] metadataBuffer = new byte[metadataSize];
                            fs.Read(metadataBuffer, 0, (int)metadataSize);
                            string json = Encoding.UTF8.GetString(metadataBuffer);
                            var metadata = JsonConvert.DeserializeObject<GameUploadData>(json);

                            // 3. Extract Zip
                            // The ZipArchive starts immediately after metadata
                            using (var archive = new ZipArchive(fs, ZipArchiveMode.Read))
                            {
                                foreach (var entry in archive.Entries)
                                {
                                    // Expand path (reversing %GAMEPATH% if needed)
                                    string relativePath = entry.FullName;
                                    string fullPath = PathContractor.ExpandPath(relativePath, gameDirectory ?? destinationDirectory);

                                    // Ensure directory exists
                                    string? parentDir = Path.GetDirectoryName(fullPath);
                                    if (!string.IsNullOrEmpty(parentDir)) Directory.CreateDirectory(parentDir);

                                    entry.ExtractToFile(fullPath, overwrite: true);
                                }
                            }

                            return new UnpackResult { Success = true, Metadata = metadata };
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugConsole.WriteException(ex, "Failed to unpack .sta archive");
                    return new UnpackResult { Success = false, Error = ex.Message };
                }
            });
        }
    }
}
