using System;
using System.Collections.Generic;

namespace SaveTracker.Resources.HELPERS
{
    public static class DebugConsole
    {
        private static bool _isEnabled = true;
        private static readonly object _lock = new();

        // Constants for colors (kept for mapped logic if needed in future, but mapping to ConsoleColor now)
        private const string COLOR_DEFAULT = "#D4D4D4";
        private const string COLOR_INFO = "#4EC9B0";
        private const string COLOR_WARNING = "#DCDCAA";
        private const string COLOR_ERROR = "#F44747";
        private const string COLOR_SUCCESS = "#6A9955";
        private const string COLOR_DEBUG = "#808080";

        /// <summary>
        /// Enable or disable console debugging
        /// </summary>
        public static void Enable(bool enable = true)
        {
            _isEnabled = enable;
        }

        /// <summary>
        /// Check if console debugging is enabled
        /// </summary>
        public static bool IsEnabled => _isEnabled;

        /// <summary>
        /// Show the console window (No-op in native console mode)
        /// </summary>
        public static void ShowConsole()
        {
            // No-op: App is now a Console App, so console is always there.
        }

        /// <summary>
        /// Hide the console window (No-op in native console mode)
        /// </summary>
        public static void HideConsole()
        {
            // No-op
        }

        /// <summary>
        /// Close and free the console (No-op)
        /// </summary>
        public static void CloseConsole()
        {
            // No-op
        }

        /// <summary>
        /// Write a message to console if enabled
        /// </summary>
        public static void WriteLine(string message = "", string title = "DATA")
        {
            if (!_isEnabled) return;
            Log(message, title, ConsoleColor.Gray);
        }

        /// <summary>
        /// Internal log method to handle timestamp and colors
        /// </summary>
        private static void Log(string message, string title, ConsoleColor color)
        {
            if (!_isEnabled) return;
            lock (_lock)
            {
                try
                {
                    string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

                    // Write Timestamp and Title in default/darker color
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($"[{timestamp} | {title}] ");

                    // Write Message in specific color
                    Console.ForegroundColor = color;
                    Console.WriteLine(message);

                    // Reset
                    Console.ResetColor();
                }
                catch { }
            }
        }

        /// <summary>
        /// Write an info message
        /// </summary>
        public static void WriteInfo(string message, string title = "INFO")
        {
            Log(message, title, ConsoleColor.Cyan);
        }

        /// <summary>
        /// Write a warning message
        /// </summary>
        public static void WriteWarning(string message, string title = "WARNING")
        {
            Log(message, title, ConsoleColor.Yellow);
        }

        /// <summary>
        /// Write an error message
        /// </summary>
        public static void WriteError(string message, string title = "ERROR")
        {
            Log(message, title, ConsoleColor.Red);
        }

        /// <summary>
        /// Write an exception with full details
        /// </summary>
        public static void WriteException(Exception ex, string context = "")
        {
            if (!_isEnabled) return;
            string msg = $"{(!string.IsNullOrEmpty(context) ? $"{context}: " : "")}{ex.Message}\n[STACK TRACE] {ex.StackTrace}";
            Log(msg, "EXCEPTION", ConsoleColor.Red);
        }

        /// <summary>
        /// Write a success message
        /// </summary>
        public static void WriteSuccess(string message, string title = "SUCCESS")
        {
            Log(message, title, ConsoleColor.Green);
        }

        /// <summary>
        /// Write a debug message
        /// </summary>
        public static void WriteDebug(string message, string title = "DEBUG")
        {
            Log(message, title, ConsoleColor.DarkGray);
        }

        /// <summary>
        /// Write a separator line
        /// </summary>
        public static void WriteSeparator(char character = '=', int length = 50)
        {
            if (!_isEnabled) return;
            Console.WriteLine(new string(character, length));
        }

        /// <summary>
        /// Write a section header
        /// </summary>
        public static void WriteSection(string title)
        {
            if (!_isEnabled) return;
            WriteSeparator('=', 60);
            Log($"  {title.ToUpper()}", "SECTION", ConsoleColor.Cyan);
            WriteSeparator('=', 60);
        }

        /// <summary>
        /// Clear the console
        /// </summary>
        public static void Clear()
        {
            if (!_isEnabled) return;
            try
            {
                Console.Clear();
            }
            catch { } // Can fail if piped
            WriteHeader();
        }

        /// <summary>
        /// Write the initial header
        /// </summary>
        private static void WriteHeader()
        {
            WriteSeparator('=', 60);
            Log("  SAVETRACKER DEBUG CONSOLE", "INIT", ConsoleColor.Cyan);
            Log($"  Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}", "INIT", ConsoleColor.Cyan);
            WriteSeparator('=', 60);
        }

        /// <summary>
        /// Write key-value pairs
        /// </summary>
        public static void WriteKeyValue(string key, object? value)
        {
            WriteLine($"{key}: {value ?? "null"}");
        }

        /// <summary>
        /// Write a list of items
        /// </summary>
        public static void WriteList<T>(string title, System.Collections.Generic.IEnumerable<T> items, string description = "")
        {
            if (!_isEnabled) return;

            WriteLine($"{title}:");
            foreach (var item in items)
            {
                WriteLine(description != "" ? $"  - {item} | {description}" : $"  - {item}");
            }
        }
    }
}
