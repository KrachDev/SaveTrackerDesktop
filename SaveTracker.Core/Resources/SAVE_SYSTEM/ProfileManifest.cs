using System;
using System.Collections.Generic;

namespace SaveTracker.Resources.SAVE_SYSTEM
{
    public class ProfileManifest
    {
        public string ProfileId { get; set; } = string.Empty;
        public DateTime LastActive { get; set; } = DateTime.MinValue;
        public List<ManagedFile> Files { get; set; } = new List<ManagedFile>();
    }

    public class ManagedFile
    {
        /// <summary>
        /// Relative path to the file in the game directory (e.g. "Saves/slot1.sav")
        /// </summary>
        public string OriginalPath { get; set; } = string.Empty;

        /// <summary>
        /// Relative path to the backup file (e.g. "Saves/slot1.sav.ST_PROFILE.Brother")
        /// </summary>
        public string BackupPath { get; set; } = string.Empty;

        /// <summary>
        /// Hash of the file content for integrity verification
        /// </summary>
        public string Hash { get; set; } = string.Empty;

        public DateTime LastModified { get; set; } = DateTime.MinValue;
    }
}
