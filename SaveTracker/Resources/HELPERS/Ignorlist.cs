using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SaveTracker.Resources.HELPERS
{
    public class Ignorlist
    {
        public static readonly string UserProfile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile
        );
        // Optimized file filtering for save game detection
        public static readonly HashSet<string> IgnoredDirectoriesSet = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        ) {
            // System and program directories
            @"C:\Windows",
            @"C:\Program Files",
            @"C:\Program Files (x86)",
            @"C:\ProgramData",
            @"C:\System Volume Information",
            @"C:\$Recycle.Bin",
            @"C:\Recovery",
            // Temp & user-local system folders
            Path.Combine(UserProfile, @"AppData\Local\Temp"),
            Path.Combine(UserProfile, @"AppData\Local\Packages"),
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\Windows"),
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\WindowsApps"),
            Path.Combine(UserProfile, @"AppData\LocalLow\Microsoft\CryptnetUrlCache"),
            @"C:\Temp",
            @"C:\Tmp",
            @"C:\Windows\Temp",
            // Graphics card caches and drivers
            Path.Combine(UserProfile, @"AppData\Local\AMD\VkCache"),
            Path.Combine(UserProfile, @"AppData\Local\AMD\DxCache"),
            Path.Combine(UserProfile, @"AppData\Local\AMD"),
            Path.Combine(UserProfile, @"AppData\Local\AMD\GLCache"),
            Path.Combine(UserProfile, @"AppData\Local\NVIDIA Corporation\NV_Cache"),
            Path.Combine(UserProfile, @"AppData\Local\NVIDIA Corporation\GLCache"),
            Path.Combine(UserProfile, @"AppData\Local\NVIDIA Corporation\DXCache"),
            Path.Combine(UserProfile, @"AppData\Local\NVIDIA"),
            Path.Combine(UserProfile, @"AppData\Local\Intel\ShaderCache"),
            Path.Combine(UserProfile, @"AppData\Local\Intel"),
            @"C:\ProgramData\NVIDIA Corporation\Drs",
            @"C:\ProgramData\NVIDIA Corporation\NV_Cache",
            @"C:\ProgramData\NVIDIA Corporation\GLCache",
            @"C:\ProgramData\NVIDIA Corporation\Downloader",
            @"C:\ProgramData\AMD\PPC",
            // DirectX and graphics caches
            Path.Combine(UserProfile, @"AppData\Local\D3DSCache"),
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\DirectX"),
            Path.Combine(UserProfile, @"AppData\Local\VirtualStore\ProgramData\Microsoft\DirectX"),
            // Game platform caches and logs
            Path.Combine(UserProfile, @"AppData\Local\Steam\htmlcache"),
            Path.Combine(UserProfile, @"AppData\Local\Steam\logs"),
            Path.Combine(UserProfile, @"AppData\Local\Steam\crashdumps"),
            Path.Combine(UserProfile, @"AppData\Local\Steam\shader_cache_temp_dir_d3d11"),
            Path.Combine(UserProfile, @"AppData\Local\Steam\shader_cache_temp_dir_vulkan"),
            Path.Combine(UserProfile, @"AppData\Local\EpicGamesLauncher\Intermediate"),
            Path.Combine(UserProfile, @"AppData\Local\EpicGamesLauncher\Saved\Logs"),
            Path.Combine(UserProfile, @"AppData\Local\EpicGamesLauncher\Saved\webcache"),
            Path.Combine(UserProfile, @"AppData\Local\Origin\Logs"),
            Path.Combine(UserProfile, @"AppData\Local\Ubisoft Game Launcher\logs"),
            Path.Combine(UserProfile, @"AppData\Local\Battle.net\Logs"),
            Path.Combine(UserProfile, @"AppData\Local\GOG.com\Galaxy\logs"),
            // Windows and system caches
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\Windows\WebCache"),
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\Windows\Caches"),
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\Windows\History"),
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\Windows\INetCache"),
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\Windows\INetCookies"),
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\Windows\IECompatCache"),
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\Windows\IECompatUaCache"),
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\Windows\IEDownloadHistory"),
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\Windows\WER"),
            // Browser caches
            Path.Combine(UserProfile, @"AppData\Local\Google\Chrome\User Data\Default\Cache"),
            Path.Combine(UserProfile, @"AppData\Local\Microsoft\Edge\User Data\Default\Cache"),
            Path.Combine(UserProfile, @"AppData\Local\Mozilla\Firefox\Profiles"),
            // Additional common directories
            Path.Combine(UserProfile, @"AppData\Local\CrashDumps"),
            Path.Combine(UserProfile, @"AppData\Local\Logs"),
            Path.Combine(UserProfile, @"AppData\Local\VirtualStore"),
        };

        // Fast file extension and name filters
        public static readonly HashSet<string> IgnoredExtensions = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        ) {
            ".tmp",
            ".log",
            ".dmp",
            ".crash",
            ".old",
            ".lock",
            ".pid",
            ".swp",
            ".swo",
            ".temp",
            ".cache",
            ".etl",
            ".evtx",
            ".pdb",
            ".map",
            ".symbols",
            ".debug",
            ".parc",
            ".exe"
        };

        public static readonly HashSet<string> IgnoredFileNames = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        ) {
            "thumbs.db",
            "desktop.ini",
            ".ds_store",
            "hiberfil.sys",
            "pagefile.sys",
            "swapfile.sys",
            "bootmgfw.efi",
            "ntuser.dat",
            "ntuser.pol"
        };

        // Simple keyword-based filters for obvious non-saves
        public static readonly string[] IgnoredKeywords =
        {
            "cache",
            "temp",
            "log",
            "crash",
            "dump",
            "shader",
            "debug",
            "thumbnail",
            "preview",
            "backup",
            "unity",
            "analytics",
            "windows",
            "config",
            "sentry",
            "sentrynative",
            "event"
        };
    }
}