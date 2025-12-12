namespace SaveTracker.Models
{
    /// <summary>
    /// Mode for Smart Sync window operation
    /// </summary>
    public enum SmartSyncMode
    {
        /// <summary>
        /// Shown before launching a game
        /// </summary>
        Launch,

        /// <summary>
        /// Shown when user clicks "Sync Now" button
        /// </summary>
        ManualSync,

        /// <summary>
        /// Shown after game exits with auto-upload
        /// </summary>
        GameExit
    }
}
