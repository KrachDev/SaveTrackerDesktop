using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;

namespace SaveTracker.Views
{
    public partial class DebugConsoleWindow : Window
    {
        public DebugConsoleWindow()
        {
            InitializeComponent();

            // Handle closing to just hide instead of actually closing, if we want to reuse it.
            // But for simplicity, we might let it close and recreate it, or better yet, just hide it.
            Closing += DebugConsoleWindow_Closing;
        }

        private void DebugConsoleWindow_Closing(object? sender, WindowClosingEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

        public void AppendLog(string message)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (LogOutput != null)
                {
                    LogOutput.Text += message + Environment.NewLine;
                    LogOutput.CaretIndex = LogOutput.Text.Length;
                }
            });
        }

        public void ClearLog()
        {
            Dispatcher.UIThread.InvokeAsync(() =>
           {
               if (LogOutput != null)
               {
                   LogOutput.Text = string.Empty;
               }
           });
        }

        private void ClearButton_Click(object? sender, RoutedEventArgs e)
        {
            ClearLog();
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e)
        {
            Hide();
        }
    }
}
