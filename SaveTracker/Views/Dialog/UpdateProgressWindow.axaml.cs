using Avalonia.Controls;
using Avalonia.Threading;
using System;

namespace SaveTracker.Views.Dialog
{
    public partial class UpdateProgressWindow : Window
    {
        public UpdateProgressWindow()
        {
            InitializeComponent();
        }

        public void UpdateProgress(double percent)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var progressBar = this.FindControl<ProgressBar>("UpdateProgressBar");
                if (progressBar != null)
                {
                    progressBar.Value = percent;
                }

                var statusText = this.FindControl<TextBlock>("StatusText");
                if (statusText != null)
                {
                    statusText.Text = $"Downloading... {percent:F0}%";
                }
            });
        }

        public void UpdateStatus(string status)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var statusText = this.FindControl<TextBlock>("StatusText");
                if (statusText != null)
                {
                    statusText.Text = status;
                }
            });
        }
    }
}
