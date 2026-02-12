using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SaveTracker.Resources.HELPERS;

namespace SaveTracker.Resources.LOGIC
{
    public class BlacklistManager
    {
        private static readonly Lazy<BlacklistManager> _instance = new Lazy<BlacklistManager>(() => new BlacklistManager());
        public static BlacklistManager Instance => _instance.Value;

        public HashSet<string> Directories { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Extensions { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> FileNames { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Keywords { get; private set; } = new();

        private readonly string _configPath;

        private BlacklistManager()
        {
            _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "blacklist_config.json");
            Load();
        }

        public void Load()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    string json = File.ReadAllText(_configPath);
                    var data = JsonSerializer.Deserialize<BlacklistData>(json);

                    if (data != null)
                    {
                        Directories = new HashSet<string>(data.Directories ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                        Extensions = new HashSet<string>(data.Extensions ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                        FileNames = new HashSet<string>(data.FileNames ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
                        Keywords = data.Keywords ?? new List<string>();
                        DebugConsole.WriteInfo("Blacklist loaded from config.");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"Failed to load blacklist config: {ex.Message}. Using defaults.");
            }

            // Fallback to defaults
            Directories = new HashSet<string>(Ignorlist.IgnoredDirectoriesSet, StringComparer.OrdinalIgnoreCase);
            Extensions = new HashSet<string>(Ignorlist.IgnoredExtensions, StringComparer.OrdinalIgnoreCase);
            FileNames = new HashSet<string>(Ignorlist.IgnoredFileNames, StringComparer.OrdinalIgnoreCase);
            Keywords = Ignorlist.IgnoredKeywords.ToList();
        }

        public async Task SaveAsync()
        {
            try
            {
                var data = new BlacklistData
                {
                    Directories = Directories.ToList(),
                    Extensions = Extensions.ToList(),
                    FileNames = FileNames.ToList(),
                    Keywords = Keywords
                };

                string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_configPath, json);
                DebugConsole.WriteSuccess("Blacklist configuration saved.");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteError($"Failed to save blacklist config: {ex.Message}");
            }
        }

        public bool AddDirectory(string path)
        {
            if (Directories.Add(path))
            {
                _ = SaveAsync();
                return true;
            }
            return false;
        }

        public bool RemoveDirectory(string path)
        {
            // Case-insensitive removal
            var key = Directories.FirstOrDefault(d => string.Equals(d, path, StringComparison.OrdinalIgnoreCase));
            if (key != null && Directories.Remove(key))
            {
                _ = SaveAsync();
                return true;
            }
            return false;
        }

        public bool AddExtension(string ext)
        {
            if (Extensions.Add(ext))
            {
                _ = SaveAsync();
                return true;
            }
            return false;
        }

        public bool RemoveExtension(string ext)
        {
            var key = Extensions.FirstOrDefault(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
            if (key != null && Extensions.Remove(key))
            {
                _ = SaveAsync();
                return true;
            }
            return false;
        }

        public bool AddFileName(string name)
        {
            if (FileNames.Add(name))
            {
                _ = SaveAsync();
                return true;
            }
            return false;
        }

        public bool RemoveFileName(string name)
        {
            var key = FileNames.FirstOrDefault(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));
            if (key != null && FileNames.Remove(key))
            {
                _ = SaveAsync();
                return true;
            }
            return false;
        }

        public bool AddKeyword(string keyword)
        {
            if (!Keywords.Contains(keyword, StringComparer.OrdinalIgnoreCase))
            {
                Keywords.Add(keyword);
                _ = SaveAsync();
                return true;
            }
            return false;
        }

        public bool RemoveKeyword(string keyword)
        {
            var key = Keywords.FirstOrDefault(k => string.Equals(k, keyword, StringComparison.OrdinalIgnoreCase));
            if (key != null)
            {
                Keywords.Remove(key);
                _ = SaveAsync();
                return true;
            }
            return false;
        }

        private class BlacklistData
        {
            public List<string> Directories { get; set; } = new();
            public List<string> Extensions { get; set; } = new();
            public List<string> FileNames { get; set; } = new();
            public List<string> Keywords { get; set; } = new();
        }
    }
}
