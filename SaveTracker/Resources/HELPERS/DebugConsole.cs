using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

// Define default LogMessage class here if not accessible, but it is in Views namespace.
// Using string hex codes for colors to pass to View.

namespace SaveTracker.Resources.HELPERS
{
    public static class DebugConsole
    {
        private static bool _isEnabled;
        private static SaveTracker.Views.DebugConsoleWindow? _consoleWindow;

        // Constants for colors
        private const string COLOR_DEFAULT = "#D4D4D4";
        private const string COLOR_INFO = "#4EC9B0"; // VS Cyan
        private const string COLOR_WARNING = "#DCDCAA"; // VS Yellow
        private const string COLOR_ERROR = "#F44747"; // VS Red
        private const string COLOR_SUCCESS = "#6A9955"; // VS Green
        private const string COLOR_DEBUG = "#808080"; // Gray

        /// <summary>
        /// Enable or disable console debugging
        /// </summary>
        public static void Enable(bool enable = true)
        {
            _isEnabled = enable;

            if (enable)
            {
                ShowConsole();
            }
            else
            {
                HideConsole();
            }
        }

        /// <summary>
        /// Check if console debugging is enabled
        /// </summary>
        public static bool IsEnabled => _isEnabled;

        /// <summary>
        /// Show the console window
        /// </summary>
        public static void ShowConsole()
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_consoleWindow == null)
                {
                    _consoleWindow = new SaveTracker.Views.DebugConsoleWindow();
                }
                try
                {
                    _consoleWindow.Show();
                    _consoleWindow.Activate();
                }
                catch
                {
                    // If window was closed externally, recreate it
                    _consoleWindow = new SaveTracker.Views.DebugConsoleWindow();
                    _consoleWindow.Show();
                }
            });

            if (!_isEnabled) _isEnabled = true;

            // Only write header if we haven't already
            // Actually, WriteHeader is safe to call multiple times if we cleared, but maybe just on init
            // Let's write it if window is fresh? No easy way to know. 
            // Stick to simplistic logic.
        }

        /// <summary>
        /// Hide the console window
        /// </summary>
        public static void HideConsole()
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                _consoleWindow?.Hide();
            });
        }

        /// <summary>
        /// Close and free the console
        /// </summary>
        public static void CloseConsole()
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                _consoleWindow?.Close();
                _consoleWindow = null;
            });
        }

        /// <summary>
        /// Write a message to console if enabled
        /// </summary>
        public static void WriteLine(string message = "", string title = "DATA")
        {
            if (!_isEnabled) return;
            Log(message, title, COLOR_DEFAULT);
        }

        /// <summary>
        /// Internal log method to handle timestamp and dispatch
        /// </summary>
        private static void Log(string message, string title, string colorHex)
        {
            if (!_isEnabled) return;
            try
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                string logMsg = $"[{timestamp} | {title}] {message}";

                // Print to stdout for debugging the debugger (or if run from terminal)
                Console.WriteLine(logMsg);

                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_consoleWindow != null)
                    {
                        try
                        {
                            _consoleWindow.AppendLog(logMsg, colorHex);
                        }
                        catch
                        {
                            // If window is closed/disposed, we might fail.
                        }
                    }
                });
            }
            catch { }
        }

        /// <summary>
        /// Write an info message with INFO prefix
        /// </summary>
        public static void WriteInfo(string message, string title = "INFO")
        {
            Log(message, title, COLOR_INFO);
        }

        /// <summary>
        /// Write a warning message with WARNING prefix
        /// </summary>
        public static void WriteWarning(string message, string title = "WARNING")
        {
            Log(message, title, COLOR_WARNING);
        }

        /// <summary>
        /// Write an error message with ERROR prefix
        /// </summary>
        public static void WriteError(string message, string title = "ERROR")
        {
            Log(message, title, COLOR_ERROR);
        }

        /// <summary>
        /// Write an exception with full details
        /// </summary>
        public static void WriteException(Exception ex, string context = "")
        {
            if (!_isEnabled) return;
            string msg = $"{(!string.IsNullOrEmpty(context) ? $"{context}: " : "")}{ex.Message}\n[STACK TRACE] {ex.StackTrace}";
            Log(msg, "EXCEPTION", COLOR_ERROR);
        }

        /// <summary>
        /// Write a success message with SUCCESS prefix
        /// </summary>
        public static void WriteSuccess(string message, string title = "SUCCESS")
        {
            Log(message, title, COLOR_SUCCESS);
        }

        /// <summary>
        /// Write a debug message with DEBUG prefix
        /// </summary>
        public static void WriteDebug(string message, string title = "DEBUG")
        {
            Log(message, title, COLOR_DEBUG);
        }

        /// <summary>
        /// Write a separator line
        /// </summary>
        public static void WriteSeparator(char character = '=', int length = 50)
        {
            string line = new string(character, length);
            Log(line, "---", COLOR_DEFAULT);
        }

        /// <summary>
        /// Write a section header
        /// </summary>
        public static void WriteSection(string title)
        {
            if (!_isEnabled) return;
            // WriteLine(); // Blank line
            WriteSeparator('=', 60);
            Log($"  {title.ToUpper()}", "SECTION", COLOR_INFO);
            WriteSeparator('=', 60);
        }

        /// <summary>
        /// Clear the console
        /// </summary>
        public static void Clear()
        {
            if (!_isEnabled) return;

            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                _consoleWindow?.ClearLog();
            });
            WriteHeader();
        }

        /// <summary>
        /// Write the initial header
        /// </summary>
        private static void WriteHeader()
        {
            WriteSeparator('=', 60);
            Log("  SAVETRACKER DEBUG CONSOLE", "INIT", COLOR_INFO);
            Log($"  Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}", "INIT", COLOR_INFO);
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
