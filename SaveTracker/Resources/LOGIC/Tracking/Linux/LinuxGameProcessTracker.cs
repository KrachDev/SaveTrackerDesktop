using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SaveTracker.Resources.HELPERS;

namespace SaveTracker.Resources.LOGIC.Tracking.Linux
{
    public class LinuxGameProcessTracker : IGameProcessTracker
    {
        public async Task<ProcessInfo?> FindGameProcess(string executableNameOrPath)
        {
            return await Task.Run(() =>
            {
                var targetName = Path.GetFileName(executableNameOrPath).ToLower();
                var pids = LinuxUtils.GetAllProcesses();

                foreach (var pid in pids)
                {
                    string cmdlinePath = Path.Combine("/proc", pid.ToString(), "cmdline");
                    string cmdline = "";
                    try
                    {
                        cmdline = File.ReadAllText(cmdlinePath).TrimEnd('\0');
                    }
                    catch (IOException)
                    {
                        // Process might have exited, or permission denied
                        continue;
                    }
                    // Check if command line contains the exe name
                    // The python script does: if arg.lower().endswith(exe_name.lower())

                    var args = cmdline.Split(' ');
                    foreach (var arg in args)
                    {
                        if (arg.ToLower().EndsWith(targetName))
                        {
                            // Found it
                            return new ProcessInfo
                            {
                                Id = pid,
                                Name = Path.GetFileName(arg),
                                Arguments = cmdline,
                                ExecutablePath = LinuxUtils.ResolveProcPath(pid),
                                EnvironmentalVariables = LinuxUtils.ReadProcEnviron(pid)
                            };
                        }
                    }
                }
                return null;
            });
        }

        public async Task<string> DetectLauncher(ProcessInfo processInfo)
        {
            return await Task.Run(() =>
            {
                var family = GetAncestorsAndDescendants(processInfo.Id);

                foreach (var proc in family)
                {
                    string cmdline = proc.Arguments.ToLower();
                    string exe = proc.ExecutablePath.ToLower();

                    if (cmdline.Contains("steam") || exe.Contains("steam")) return "Steam/Proton";
                    if (cmdline.Contains("lutris") || exe.Contains("lutris")) return "Lutris";
                    if (cmdline.Contains("heroic") || exe.Contains("heroic")) return "Heroic";
                    if (cmdline.Contains("bottles") || exe.Contains("bottles")) return "Bottles";
                    if (exe.Contains("wine") || cmdline.Contains("wine")) return "Wine";
                }

                return "Unknown";
            });
        }

        public async Task<string?> DetectGamePrefix(ProcessInfo processInfo)
        {
            return await Task.Run(() =>
            {
                var family = GetAncestorsAndDescendants(processInfo.Id);

                // Method 1: Environment Variables (WINEPREFIX or STEAM_COMPAT_DATA_PATH)
                foreach (var proc in family)
                {
                    if (proc.EnvironmentalVariables.TryGetValue("WINEPREFIX", out string prefix) && IsValidPrefix(prefix))
                    {
                        DebugConsole.WriteLine($"Found Prefix via WINEPREFIX (PID {proc.Id}): {prefix}");
                        return prefix;
                    }

                    if (proc.EnvironmentalVariables.TryGetValue("STEAM_COMPAT_DATA_PATH", out string steamDataPath))
                    {
                        string pfx = Path.Combine(steamDataPath, "pfx");
                        if (IsValidPrefix(pfx))
                        {
                            DebugConsole.WriteLine($"Found Prefix via STEAM_COMPAT_DATA_PATH (PID {proc.Id}): {pfx}");
                            return pfx;
                        }
                    }
                }

                // Method 2: Check /proc filesystem
                foreach (var proc in family)
                {
                    string prefix = FindPrefixFromProc(proc.Id);
                    if (!string.IsNullOrEmpty(prefix))
                    {
                        DebugConsole.WriteLine($"Found Prefix via /proc (PID {proc.Id}): {prefix}");
                        return prefix;
                    }
                }

                // Method 3: Default Wine
                string defaultPrefix = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wine");
                if (IsValidPrefix(defaultPrefix))
                {
                    DebugConsole.WriteLine("Using default Wine prefix");
                    return defaultPrefix;
                }

                return null;
            });
        }

        private List<ProcessInfo> GetAncestorsAndDescendants(int pid)
        {
            var family = new List<ProcessInfo>();

            // Current
            var current = GetProcessInfo(pid);
            if (current != null) family.Add(current);

            // Ancestors
            int currentPid = pid;
            while (true)
            {
                int ppid = LinuxUtils.GetParentPid(currentPid);
                if (ppid <= 0 || ppid == currentPid) break;

                var parent = GetProcessInfo(ppid);
                if (parent != null)
                {
                    family.Add(parent);
                    currentPid = ppid;
                }
                else
                {
                    break;
                }
            }

            // Descendants (Simple 1-level check for now, or expensive full scan)
            // Python script does recursive children.
            // On Linux, to find children, we scan all processes and check their PPID.
            // This is expensive if we do it recursively.
            // For now, let's just do one pass of all processes to find children of our main family members?
            // Or just scan all processes once and build a tree?
            // Let's scan all processes once to find direct children of the definition PID.
            // The Python script is thorough. Let's be reasonably thorough but efficient.
            // Just scanning all processes once is O(N).

            var allPids = LinuxUtils.GetAllProcesses();
            foreach (var p in allPids)
            {
                if (family.Any(x => x.Id == p)) continue; // Already in family

                int parent = LinuxUtils.GetParentPid(p);
                if (family.Any(x => x.Id == parent))
                {
                    var child = GetProcessInfo(p);
                    if (child != null) family.Add(child);
                }
            }

            return family;
        }

        private ProcessInfo? GetProcessInfo(int pid)
        {
            try
            {
                string cmd = LinuxUtils.ReadProcCmdLine(pid);
                if (string.IsNullOrEmpty(cmd)) return null;

                return new ProcessInfo
                {
                    Id = pid,
                    Arguments = cmd,
                    ExecutablePath = LinuxUtils.ResolveProcPath(pid),
                    EnvironmentalVariables = LinuxUtils.ReadProcEnviron(pid)
                };
            }
            catch
            {
                return null;
            }
        }

        private string? FindPrefixFromProc(int pid)
        {
            string root = $"/proc/{pid}/root";
            if (!Directory.Exists(root)) return null;

            // This is tricky because we can't easily walk "up" from a symlink target in .NET 
            // without resolving it first or treating it as a literal path.
            // In the python script: it walks up from `root` variable? No, `root` is the search base.
            // Actually the python script checks if `root` itself is a prefix?
            // "Search upwards from root" -> This seems to imply looking at where /proc/{pid}/root points to.
            // /proc/{pid}/root IS the root directory of the process. If it's a bubblewrap/container, it might differ.
            // But usually it just points to /.

            // The Python script logic:
            // start = root (/proc/pid/root)
            // current = start
            // while True: if is_valid_prefix(current)...

            // The python script logic seems to assume that if we are inside a container/chroot, 
            // we traverse up from that chroot root? That seems weird unless the chroot IS the prefix.

            // Wait, looking at the Python script again:
            /*
             while True:
                if self.is_valid_prefix(current):
                    return current
                parent = os.path.dirname(current)
            */
            // Since `/proc/{pid}/root` is a symlink to `/`, iterating up from `/` on the host just checks `/`, then `/`, etc.
            // UNLESS `root` points to a specific directory (like inside a Flatpak or Proton container).
            // So we should resolve the link target of `/proc/{pid}/root`.

            try
            {
                var linkInfo = new FileInfo(root);
                string realRoot = linkInfo.LinkTarget ?? root; // .NET 6+

                // If it's just "/", then we are scanning the whole host filesystem? That's bad.
                // But the Python script "Try common paths" logic is safer.

                string[] commonPaths = { "pfx", "prefix", ".wine", "drive_c/.." };

                // We will try to scan specific locations relative to the process CWD maybe?
                // Or just the `cwd` of the process.

                string cwd = $"/proc/{pid}/cwd";
                string? realCwd = null;
                try { realCwd = new FileInfo(cwd).LinkTarget; } catch { }

                if (realCwd != null)
                {
                    // Scan upwards from CWD
                    var dir = new DirectoryInfo(realCwd);
                    while (dir != null)
                    {
                        if (IsValidPrefix(dir.FullName)) return dir.FullName;
                        if (dir.Parent == null) break;
                        dir = dir.Parent;
                    }
                }
            }
            catch { }

            return null;
        }

        private bool IsValidPrefix(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return false;

            bool hasSystemReg = File.Exists(Path.Combine(path, "system.reg"));
            bool hasUserReg = File.Exists(Path.Combine(path, "user.reg"));
            bool hasDriveC = Directory.Exists(Path.Combine(path, "drive_c"));

            return hasSystemReg && (hasUserReg || hasDriveC);
        }
    }
}
