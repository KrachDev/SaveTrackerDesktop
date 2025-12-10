using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SaveTracker.Resources.LOGIC.Tracking
{
    public class ProcessInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Arguments { get; set; } = "";
        public string ExecutablePath { get; set; } = "";
        public Dictionary<string, string> EnvironmentalVariables { get; set; } = new();

        public override string ToString()
        {
            return $"{Name} ({Id})";
        }
    }

    public interface IGameProcessTracker
    {
        /// <summary>
        /// Finds the main game process based on the expected executable name or path.
        /// </summary>
        /// <param name="executableNameOrPath">The name (e.g., "game.exe") or full path.</param>
        /// <returns>ProcessInfo if found, otherwise null.</returns>
        Task<ProcessInfo?> FindGameProcess(string executableNameOrPath);

        /// <summary>
        /// Attempts to detect the installation prefix (e.g., Wine prefix, Proton prefix) for the given process.
        /// </summary>
        /// <param name="processInfo">The process to analyze.</param>
        /// <returns>The path to the prefix if found, otherwise null.</returns>
        Task<string?> DetectGamePrefix(ProcessInfo processInfo);

        /// <summary>
        /// Attempts to detect the launcher or compatibility layer used (e.g., Steam/Proton, Lutris, Wine).
        /// </summary>
        /// <param name="processInfo">The process to analyze.</param>
        /// <returns>Name of the launcher/tool, or "Unknown".</returns>
        Task<string> DetectLauncher(ProcessInfo processInfo);
    }
}
