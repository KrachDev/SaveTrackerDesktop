namespace SaveTracker.Models
{
    /// <summary>
    /// Mode for Smart Sync window operation
    /// </summary>
    public enum SmartSyncMode
    {
        /// <summary>
        /// Shown when user clicks "Sync Now" button
        /// </summary>
        ManualSync,

        /// <summary>
        /// Shown after game exits with auto-upload
        /// </summary>
        GameExit,

        /// <summary>
        /// Shown before game launch
        /// </summary>
        GameLaunch
    }
}
