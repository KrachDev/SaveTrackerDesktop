using System;
using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace SaveTracker.Resources.HELPERS
{
    /// <summary>
    /// Generates a unique, anonymous hardware ID for user tracking
    /// </summary>
    public static class HardwareId
    {
        private static string? _cachedId;

        /// <summary>
        /// Gets a unique hardware ID based on CPU, motherboard, and disk
        /// This is anonymous and doesn't contain personal information
        /// </summary>
        public static string GetHardwareId()
        {
            if (_cachedId != null)
                return _cachedId;

            try
            {
                string cpuId = GetCpuId();
                string motherboardId = GetMotherboardId();
                string diskId = GetDiskId();

                // Combine and hash to create anonymous ID
                string combined = $"{cpuId}-{motherboardId}-{diskId}";
                _cachedId = HashString(combined);

                return _cachedId;
            }
            catch
            {
                // Fallback to machine name hash if WMI fails
                _cachedId = HashString(Environment.MachineName + Environment.UserName);
                return _cachedId;
            }
        }

        private static string GetCpuId()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                    return obj["ProcessorId"]?.ToString() ?? "";
                }
            }
            catch { }
            return "";
        }

        private static string GetMotherboardId()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard");
                foreach (ManagementObject obj in searcher.Get())
                {
                    return obj["SerialNumber"]?.ToString() ?? "";
                }
            }
            catch { }
            return "";
        }

        private static string GetDiskId()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_DiskDrive");
                foreach (ManagementObject obj in searcher.Get())
                {
                    return obj["SerialNumber"]?.ToString() ?? "";
                }
            }
            catch { }
            return "";
        }

        private static string HashString(string input)
        {
            using var sha256 = SHA256.Create();
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));

            // Convert to short hex string (first 16 characters)
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 8; i++)
            {
                sb.Append(bytes[i].ToString("x2"));
            }
            return sb.ToString();
        }
    }
}
