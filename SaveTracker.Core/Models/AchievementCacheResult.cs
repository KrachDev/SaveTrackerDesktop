using System.Collections.Generic;

namespace SaveTracker.Models;

/// <summary>
/// Represents the result of achievement caching for a single game
/// </summary>
public class AchievementCacheResult
{
    public string GameName { get; set; } = string.Empty;

    /// <summary>
    /// Status: "Success", "NeedsInput", "Failed"
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Steam App ID if successfully extracted
    /// </summary>
    public string? AppId { get; set; }

    /// <summary>
    /// Status icon for UI display
    /// </summary>
    public string StatusIcon => Status switch
    {
        "Success" => "✅",
        "NeedsInput" => "⚠️",
        "Failed" => "❌",
        _ => "❓"
    };

    /// <summary>
    /// Human-readable status text
    /// </summary>
    public string StatusText => Status switch
    {
        "Success" => $"Cached to {AppId}",
        "NeedsInput" => "Manual App ID required",
        "Failed" => "Processing failed",
        _ => "Unknown"
    };
}

/// <summary>
/// Summary of achievement caching operation
/// </summary>
public class AchievementCacheSummary
{
    public int SuccessCount { get; set; }
    public int NeedsInputCount { get; set; }
    public int FailedCount { get; set; }
    public List<AchievementCacheResult> Results { get; set; } = new List<AchievementCacheResult>();

    public int TotalGames => SuccessCount + NeedsInputCount + FailedCount;

    public bool HasIssues => NeedsInputCount > 0 || FailedCount > 0;
}
