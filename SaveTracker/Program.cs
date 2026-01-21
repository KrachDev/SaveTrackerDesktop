using Avalonia;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Svg.Skia;

namespace SaveTracker
{
    internal sealed class Program
    {
        // 1. Unique ID for your app.
        private const string UniqueAppName = "SaveTracker_SingleInstance_Mutex_ID";
        private const string PipeName = "SaveTracker_Pipe_Channel";

        private static Mutex? _mutex;

        // 2. An event other parts of your app can listen to
        public static event Action<string[]>? FilesDropped;

        [STAThread]
        public static void Main(string[] args)
        {
            // 3. Check if an instance is already running
            bool isNewInstance;
            _mutex = new Mutex(true, UniqueAppName, out isNewInstance);

            if (!isNewInstance)
            {
                // --- SECOND INSTANCE ---
                // App is already running. Send args to it and close.
                SendArgsToRunningInstance(args);
                return;
            }

            // --- FIRST INSTANCE ---
            // Start the background listener for future args
            Task.Run(StartNamedPipeServer);

            // Start Avalonia normally
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);

            // Release mutex when app closes
            _mutex.ReleaseMutex();
        }

        // --- LOGIC TO SEND ARGS (CLIENT) ---
        private static void SendArgsToRunningInstance(string[] args)
        {
            if (args.Length == 0) return;

            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(1000); // Wait 1 sec for connection

                using var writer = new StreamWriter(client);
                // We join args with a specific separator to send them over the pipe
                writer.Write(string.Join("|||", args));
                writer.Flush();
            }
            catch (Exception) { /* Ignore errors if main app is unresponsive */ }
        }

        // --- LOGIC TO RECEIVE ARGS (SERVER) ---
        private static void StartNamedPipeServer()
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In);
                    server.WaitForConnection();

                    using var reader = new StreamReader(server);
                    var text = reader.ReadToEnd();
                    var args = text.Split(new[] { "|||" }, StringSplitOptions.RemoveEmptyEntries);

                    // Send to UI Thread
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        // Trigger the event!
                        FilesDropped?.Invoke(args);

                        // Optional: Bring window to front (requires reference to MainWindow)
                        if (Application.Current?.ApplicationLifetime is
                            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                        {
                            desktop.MainWindow?.Activate();
                        }
                    });
                }
                catch { /* Handle server errors/restarts */ }
            }
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}