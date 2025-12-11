using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using System.Collections.ObjectModel;
using Avalonia.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;

namespace SaveTracker.Views
{
    public class LogMessage
    {
        public string Text { get; set; } = "";
        public IBrush Color { get; set; } = Brushes.White;
    }

    public partial class DebugConsoleWindow : Window, INotifyPropertyChanged
    {
        private ScrollViewer? _scrollViewer;
        private readonly StringBuilder _logBuilder = new();

        public ObservableCollection<LogMessage> LogMessages { get; } = new();

        private bool _isPresentationMode = true;
        public bool IsPresentationMode
        {
            get => _isPresentationMode;
            set
            {
                if (_isPresentationMode != value)
                {
                    _isPresentationMode = value;
                    OnPropertyChanged();
                    // Regenerate text when switching to copy mode
                    if (!value)
                    {
                        RegenerateLogText();
                    }
                }
            }
        }

        private bool _isAutoScrollEnabled = true;
        public bool IsAutoScrollEnabled
        {
            get => _isAutoScrollEnabled;
            set
            {
                if (_isAutoScrollEnabled != value)
                {
                    _isAutoScrollEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _logText = "";
        public string LogText
        {
            get => _logText;
            set
            {
                if (_logText != value)
                {
                    _logText = value;
                    OnPropertyChanged();
                }
            }
        }

        public new event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public DebugConsoleWindow()
        {
            InitializeComponent();
            DataContext = this;

            Closing += DebugConsoleWindow_Closing;
        }

        private void DebugConsoleWindow_Closing(object? sender, WindowClosingEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

        private void RegenerateLogText()
        {
            _logBuilder.Clear();
            foreach (var msg in LogMessages)
            {
                _logBuilder.AppendLine(msg.Text);
            }
            LogText = _logBuilder.ToString();
        }

        public void AppendLog(string message, string colorHex = "#D4D4D4")
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var color = Brush.Parse(colorHex);
                    LogMessages.Add(new LogMessage { Text = message, Color = color });

                    // Also update plain text for copy mode
                    _logBuilder.AppendLine(message);
                    LogText = _logBuilder.ToString();

                    // Auto-scroll only if enabled
                    if (IsAutoScrollEnabled && _scrollViewer != null)
                    {
                        _scrollViewer.ScrollToEnd();
                    }
                }
                catch
                {
                    // Fallback using white if color parse fails
                    LogMessages.Add(new LogMessage { Text = message, Color = Brushes.White });
                    _logBuilder.AppendLine(message);
                    LogText = _logBuilder.ToString();
                }
            });
        }

        public void ClearLog()
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                LogMessages.Clear();
                _logBuilder.Clear();
                LogText = "";
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

        // We need to capture the ScrollViewer once the template is applied or from the name
        protected override void OnApplyTemplate(Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            _scrollViewer = this.FindControl<ScrollViewer>("LogScrollViewer");
        }
    }
}
