using Avalonia;

using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SaveTracker
{
    internal class Program
    {
        // Mutex for single instance check
        private static Mutex? _mutex;
        private const string MutexName = "Global\\SaveTracker_SingleInstance_Mutex";

        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            // Ensure only one instance is running
            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);

            if (!createdNew)
            {
                // App is already running!
                // Attempt to send arguments to the existing instance via Named Pipe
                SendArgsToExistingInstance(args);

                // Exit this new instance
                return;
            }

            try
            {
                // Start the IPC Command Server and inject the Avalonia Window Manager
                var windowManager = new AvaloniaWindowManager();
                Task.Run(() => SaveTracker.Resources.LOGIC.IPC.IpcServer.StartAsync(windowManager));

                BuildAvaloniaApp()
                    .StartWithClassicDesktopLifetime(args);
            }
            finally
            {
                // Gracefully stop the IPC server
                SaveTracker.Resources.LOGIC.IPC.IpcServer.Stop();

                _mutex.ReleaseMutex();
            }
        }

        private static void SendArgsToExistingInstance(string[] args)
        {
            try
            {
                // If we were launched with arguments (e.g. "showmainwindow"), pass them to the running instance
                string command = args.Length > 0 ? string.Join(" ", args) : "showmainwindow";

                // Simple client to send command to the existing server
                using var client = new NamedPipeClientStream(".", "SaveTracker_Command_Pipe", PipeDirection.InOut);
                client.Connect(1000);

                // Send simple JSON command
                var json = $"{{\"id\":\"wake_dup\",\"command\":\"{command}\"}}";
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                client.Write(bytes, 0, bytes.Length);
            }
            catch (Exception)
            {
                // Ignore errors if we can't notify the other instance
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