using System;

namespace SaveTracker.Models
{
    /// <summary>
    /// Represents a file comparison between local and cloud saves
    /// </summary>
    public class FileComparisonItem
    {
        public string FileName { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // "Same", "Cloud Newer", "Local Newer", "Conflict", "Local Only", "Cloud Only"
        public string LocalSize { get; set; } = "-";
        public string CloudSize { get; set; } = "-";
        public DateTime? LocalModified { get; set; }
        public DateTime? CloudModified { get; set; }
        public string Icon { get; set; } = "📁"; // "✓", "↑", "↓", "⚠", "📁", "☁"
        public string LocalChecksum { get; set; } = string.Empty;
        public string CloudChecksum { get; set; } = string.Empty;
    }
}
