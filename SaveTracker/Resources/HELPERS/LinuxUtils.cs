using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SaveTracker.Resources.HELPERS
{
    public static class LinuxUtils
    {
        public static string ReadProcCmdLine(int pid)
        {
            try
            {
                string path = $"/proc/{pid}/cmdline";
                if (File.Exists(path))
                {
                    // cmdline is null-terminated strings
                    var bytes = File.ReadAllBytes(path);
                    return Encoding.Default.GetString(bytes).Replace('\0', ' ').Trim();
                }
            }
            catch { }
            return "";
        }

        public static Dictionary<string, string> ReadProcEnviron(int pid)
        {
            var env = new Dictionary<string, string>();
            try
            {
                string path = $"/proc/{pid}/environ";
                if (File.Exists(path))
                {
                    // environ is key=val null-terminated strings
                    var bytes = File.ReadAllBytes(path);
                    var text = Encoding.Default.GetString(bytes);
                    var parts = text.Split('\0', StringSplitOptions.RemoveEmptyEntries);

                    foreach (var part in parts)
                    {
                        var eqIndex = part.IndexOf('=');
                        if (eqIndex > 0)
                        {
                            string key = part.Substring(0, eqIndex);
                            string val = part.Substring(eqIndex + 1);
                            if (!env.ContainsKey(key))
                                env[key] = val;
                        }
                    }
                }
            }
            catch { }
            return env;
        }

        public static int GetParentPid(int pid)
        {
            try
            {
                string path = $"/proc/{pid}/stat";
                if (File.Exists(path))
                {
                    string content = File.ReadAllText(path);
                    // stat format is complex, (filename) can contain spaces/parens.
                    // The safest way is to find the last ')' and parse from there.
                    int lastParen = content.LastIndexOf(')');
                    if (lastParen != -1 && lastParen + 2 < content.Length)
                    {
                        var parts = content.Substring(lastParen + 2).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            // 4th field in spec, but we split after name, so it's the 2nd field (index 1)
                            // "state ppid pgrp..."
                            // actually: (2) state, (3) ppid. So parts[1] is PPID.
                            if (int.TryParse(parts[1], out int ppid))
                            {
                                return ppid;
                            }
                        }
                    }
                }
            }
            catch { }
            return -1;
        }

        public static string ResolveProcPath(int pid)
        {
            try
            {
                string path = $"/proc/{pid}/exe";
                if (File.Exists(path))
                {
                    // In C#, File.ResolveLinkTarget is .NET 6+.
                    // If simply checking where it points, we might need `realpath` or `readlink`.
                    // But `Path.GetFullPath` might not resolve symlinks on Linux in the way we want for /proc.
                    // Standard way is reading the symlink.
                    var linkInfo = new System.IO.FileInfo(path);
                    return linkInfo.LinkTarget ?? linkInfo.FullName;
                }
            }
            catch { }
            return "";
        }

        /// <summary>
        /// Finds the full path to an executable by searching the PATH environment variable.
        /// </summary>
        public static string? FindExecutable(string name)
        {
            try
            {
                var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator);
                if (paths == null) return null;

                foreach (var dir in paths)
                {
                    string fullPath = Path.Combine(dir, name);
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Reads all children pids (recursively or shallow) is hard without traversing all /proc.
        /// Better to iterate all /proc, find their PPID.
        /// </summary>
        public static List<int> GetAllProcesses()
        {
            var pids = new List<int>();
            try
            {
                var procDir = new DirectoryInfo("/proc");
                foreach (var dir in procDir.GetDirectories())
                {
                    if (int.TryParse(dir.Name, out int pid))
                    {
                        pids.Add(pid);
                    }
                }
            }
            catch { }
            return pids;
        }
        public static void KillProcessTree(int rootPid)
        {
            try
            {
                // Build parent map
                var allPids = GetAllProcesses();
                var parentMap = new Dictionary<int, int>();
                foreach (var pid in allPids)
                {
                    parentMap[pid] = GetParentPid(pid);
                }

                // BFS to find all descendants
                var toKill = new HashSet<int>();
                toKill.Add(rootPid);

                var queue = new Queue<int>();
                queue.Enqueue(rootPid);

                while (queue.Count > 0)
                {
                    int parent = queue.Dequeue();

                    // Find children
                    foreach (var kvp in parentMap)
                    {
                        if (kvp.Value == parent && !toKill.Contains(kvp.Key))
                        {
                            toKill.Add(kvp.Key);
                            queue.Enqueue(kvp.Key);
                        }
                    }
                }

                // Kill all identifiable processes in tree
                foreach (var pid in toKill)
                {
                    try
                    {
                        DebugConsole.WriteInfo($"[KillProcessTree] Killing PID {pid}");
                        var proc = System.Diagnostics.Process.GetProcessById(pid);
                        proc.Kill();
                    }
                    catch (Exception ex)
                    {
                        DebugConsole.WriteWarning($"[KillProcessTree] Failed to kill {pid}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"KillProcessTree Error: {ex.Message}");
            }
        }
    }
}
