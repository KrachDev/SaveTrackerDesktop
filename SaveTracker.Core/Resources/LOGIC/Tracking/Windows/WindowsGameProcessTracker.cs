using System;
using System.Diagnostics;
using System.Threading.Tasks;
using SaveTracker.Resources.HELPERS;

namespace SaveTracker.Resources.LOGIC.Tracking.Windows
{
    public class WindowsGameProcessTracker : IGameProcessTracker
    {
        public async Task<ProcessInfo?> FindGameProcess(string executableNameOrPath)
        {
            return await Task.Run(() =>
            {
                var targetName = System.IO.Path.GetFileNameWithoutExtension(executableNameOrPath).ToLower();
                var procs = Process.GetProcesses();

                foreach (var p in procs)
                {
                    try
                    {
                        if (p.ProcessName.ToLower() == targetName)
                        {
                            return new ProcessInfo
                            {
                                Id = p.Id,
                                Name = p.ProcessName,
                                ExecutablePath = p.MainModule?.FileName ?? ""
                            };
                        }
                    }
                    catch { }
                }
                return null;
            });
        }

        public Task<string?> DetectGamePrefix(ProcessInfo processInfo)
        {
            // Windows native: no prefix concept
            return Task.FromResult<string?>(null);
        }

        public Task<string> DetectLauncher(ProcessInfo processInfo)
        {
            // Could implement similar parent checking on Windows if desired
            return Task.FromResult("Windows Native");
        }
    }
}
