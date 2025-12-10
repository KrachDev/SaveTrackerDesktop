using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SaveTracker.Resources.HELPERS
{
    public static class DebugConsole
    {
        private static bool _isEnabled;
        private static bool _isConsoleAllocated;
        private static SaveTracker.Views.DebugConsoleWindow? _consoleWindow;

        private static readonly bool _isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);

        // Windows‑only P/Invoke declarations – only compiled when running on Windows
#if WINDOWS
        // Windows‑only P/Invoke declarations – only compiled when running on Windows
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool FreeConsole();

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern System.IntPtr GetConsoleWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(System.IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool SetConsoleTitle(string lpConsoleTitle);
#endif

        private const int SwHide = 0;
        private const int SwRestore = 9;

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
            if (!_isConsoleAllocated)
            {
                if (_isWindows)
                {
#if WINDOWS
                    AllocConsole();
                    SetConsoleTitle("SaveTracker Debug Console");
#endif
                }
                else
                {
                    // Linux/Mac: Use Avalonia Window
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (_consoleWindow == null)
                        {
                            _consoleWindow = new SaveTracker.Views.DebugConsoleWindow();
                        }
                        _consoleWindow.Show();
                    });
                }
                _isConsoleAllocated = true;

                // Redirect console output (works on all platforms)
                System.Console.SetOut(new StreamWriter(System.Console.OpenStandardOutput()) { AutoFlush = true });
                System.Console.SetError(new StreamWriter(System.Console.OpenStandardError()) { AutoFlush = true });

                WriteHeader();
            }
            else
            {
                if (_isWindows)
                {
#if WINDOWS
                    IntPtr consoleWindow = GetConsoleWindow();
                    if (consoleWindow != IntPtr.Zero)
                    {
                        ShowWindow(consoleWindow, SwRestore);
                    }
#endif
                }
                else
                {
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                   {
                       if (_consoleWindow == null)
                       {
                           _consoleWindow = new SaveTracker.Views.DebugConsoleWindow();
                       }
                       _consoleWindow.Show();
                       _consoleWindow.Activate();
                   });
                }
            }
        }

        /// <summary>
        /// Hide the console window
        /// </summary>
        public static void HideConsole()
        {
            if (_isWindows)
            {
#if WINDOWS
                IntPtr consoleWindow = GetConsoleWindow();
                if (consoleWindow != IntPtr.Zero)
                {
                    ShowWindow(consoleWindow, SwHide);
                }
#endif
            }
            else
            {
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _consoleWindow?.Hide();
                });
            }
            // No‑op on non‑Windows platforms
        }

        /// <summary>
        /// Close and free the console
        /// </summary>
        public static void CloseConsole()
        {
            if (_isConsoleAllocated)
            {
                if (_isWindows)
                {
#if WINDOWS
                    FreeConsole();
#endif
                }
                _isConsoleAllocated = false;
            }
        }

        /// <summary>
        /// Write a message to console if enabled
        /// </summary>
        public static void WriteLine(string message = "", string title = "DATA")
        {
            if (!_isEnabled) return;

            try
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                string logMsg = $"[{timestamp} | {title}] {message}";
                Console.WriteLine(logMsg);

                if (!_isWindows && _isConsoleAllocated)
                {
                    _consoleWindow?.AppendLog(logMsg);
                }
            }
            catch
            {
                // Ignore console errors
            }
        }

        /// <summary>
        /// Write an info message with INFO prefix
        /// </summary>
        public static void WriteInfo(string message, string title = "INFO")
        {
            WriteLine($"[{title}] {message}");
        }

        /// <summary>
        /// Write a warning message with WARNING prefix (in yellow if supported)
        /// </summary>
        public static void WriteWarning(string message, string title = "WARNING")
        {
            if (!_isEnabled) return;

            try
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                WriteLine($"[{title}] {message}");
                Console.ResetColor();
            }
            catch
            {
                WriteLine($"[WARNING] {message}");
            }
        }

        /// <summary>
        /// Write an error message with ERROR prefix (in red if supported)
        /// </summary>
        public static void WriteError(string message, string title = "ERROR")
        {
            if (!_isEnabled) return;

            try
            {
                Console.ForegroundColor = ConsoleColor.Red;
                WriteLine($"[{title}] {message}");
                Console.ResetColor();
            }
            catch
            {
                WriteLine($"[{title}] {message}");
            }
        }

        /// <summary>
        /// Write an exception with full details
        /// </summary>
        public static void WriteException(Exception ex, string context = "")
        {
            if (!_isEnabled) return;

            try
            {
                Console.ForegroundColor = ConsoleColor.Red;
                WriteLine($"[EXCEPTION] {(!string.IsNullOrEmpty(context) ? $"{context}: " : "")}{ex.Message}");
                WriteLine($"[STACK TRACE] {ex.StackTrace}");
                Console.ResetColor();
            }
            catch
            {
                WriteLine($"[EXCEPTION] {(!string.IsNullOrEmpty(context) ? $"{context}: " : "")}{ex.Message}");
                WriteLine($"[STACK TRACE] {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Write a success message with SUCCESS prefix (in green if supported)
        /// </summary>
        public static void WriteSuccess(string message, string title = "SUCCESS")
        {
            if (!_isEnabled) return;

            try
            {
                Console.ForegroundColor = ConsoleColor.Green;
                WriteLine($"[{title}] {message}");
                Console.ResetColor();
            }
            catch
            {
                WriteLine($"[SUCCESS] {message}");
            }
        }

        /// <summary>
        /// Write a debug message with DEBUG prefix (in gray if supported)
        /// </summary>
        public static void WriteDebug(string message, string title = "DEBUG")
        {
            if (!_isEnabled) return;

            try
            {
                Console.ForegroundColor = ConsoleColor.Gray;
                WriteLine($"[{title}] {message}");
                Console.ResetColor();
            }
            catch
            {
                WriteLine($"[{title}] {message}");
            }
        }

        /// <summary>
        /// Write a separator line
        /// </summary>
        public static void WriteSeparator(char character = '=', int length = 50)
        {
            WriteLine(new string(character, length));
        }

        /// <summary>
        /// Write a section header
        /// </summary>
        public static void WriteSection(string title)
        {
            if (!_isEnabled) return;

            try
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                WriteLine();
                WriteSeparator('=', 60);
                WriteLine($"  {title.ToUpper()}");
                WriteSeparator('=', 60);
                Console.ResetColor();
            }
            catch
            {
                WriteLine();
                WriteSeparator('=', 60);
                WriteLine($"  {title.ToUpper()}");
                WriteSeparator('=', 60);
            }
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
                WriteHeader();
            }
            catch
            {
                // Ignore clear errors
            }
        }

        /// <summary>
        /// Write the initial header
        /// </summary>
        private static void WriteHeader()
        {
            if (!_isEnabled) return;

            try
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                WriteSeparator('=', 60);
                WriteLine("  SAVETRACKER DEBUG CONSOLE");
                WriteLine($"  Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                WriteSeparator('=', 60);
                Console.ResetColor();
                WriteLine();
            }
            catch
            {
                WriteSeparator('=', 60);
                WriteLine("  SAVETRACKER DEBUG CONSOLE");
                WriteLine($"  Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                WriteSeparator('=', 60);
                WriteLine();
            }
        }

        /// <summary>
        /// Write key-value pairs in a formatted way
        /// </summary>
        public static void WriteKeyValue(string key, object value)
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
