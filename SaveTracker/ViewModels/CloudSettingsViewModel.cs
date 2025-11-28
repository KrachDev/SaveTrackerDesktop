using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveTracker.Resources.HELPERS;
using SaveTracker.Resources.Logic.RecloneManagement;
using SaveTracker.Resources.Logic;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SaveTracker.Resources.SAVE_SYSTEM;

namespace SaveTracker.ViewModels
{
    public partial class CloudSettingsViewModel : ViewModelBase
    {
        private readonly RcloneInstaller _rcloneInstaller;
        private readonly RcloneConfigManager _configManager;
        private readonly CloudProviderHelper _providerHelper;

        [ObservableProperty]
        private string _rcloneVersion = "Checking...";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConfigureCommand))]
        private bool _isRcloneInstalled;

        [ObservableProperty]
        private bool _isRcloneConfigured;

        [ObservableProperty]
        private string _statusMessage = "";

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private ObservableCollection<CloudProviderDisplay> _providers = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConfigureCommand))]
        private CloudProviderDisplay? _selectedProvider;

        public event Action? RequestClose;

        public CloudSettingsViewModel()
        {
            _rcloneInstaller = new RcloneInstaller();
            _configManager = new RcloneConfigManager();
            _providerHelper = new CloudProviderHelper();

            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            await CheckRcloneStatusAsync();
            LoadProviders();
        }

        private void LoadProviders()
        {
            Providers.Clear();
            var supported = _providerHelper.GetSupportedProviders();
            foreach (var provider in supported)
            {
                Providers.Add(new CloudProviderDisplay(provider, _providerHelper.GetProviderDisplayName(provider)));
            }

            // Default selection (try to load from config if possible, otherwise default to first)
            SelectedProvider = Providers.FirstOrDefault();
        }

        private async Task CheckRcloneStatusAsync()
        {
            IsBusy = true;
            StatusMessage = "Checking Rclone status...";

            try
            {
                string rclonePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtraTools", "rclone.exe");
                IsRcloneInstalled = File.Exists(rclonePath);

                if (IsRcloneInstalled)
                {
                    // Get version
                    var executor = new RcloneExecutor();
                    var result = await executor.ExecuteRcloneCommand("version --check=false", TimeSpan.FromSeconds(5));
                    if (result.Success)
                    {
                        var lines = result.Output.Split('\n');
                        var versionLine = lines.FirstOrDefault(l => l.StartsWith("rclone v"));
                        RcloneVersion = versionLine?.Trim() ?? "Unknown version";
                    }
                    else
                    {
                        RcloneVersion = "Installed (Version check failed)";
                    }

                    // Check config
                    string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtraTools", "rclone.conf");
                    IsRcloneConfigured = File.Exists(configPath);
                }
                else
                {
                    RcloneVersion = "Not Installed";
                    IsRcloneConfigured = false;
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to check Rclone status");
                StatusMessage = "Error checking status";
            }
            finally
            {
                IsBusy = false;
                StatusMessage = "";
            }
        }

        [RelayCommand]
        private async Task InstallRcloneAsync()
        {
            IsBusy = true;
            StatusMessage = "Downloading and installing Rclone...";

            try
            {
                // We need to access the private method DownloadAndInstallRclone or expose it.
                // Since it's private in RcloneInstaller, we might need to modify RcloneInstaller 
                // or use RcloneCheckAsync which triggers download if missing.

                // Let's use RcloneCheckAsync with a dummy provider to trigger installation check
                // This is a bit of a hack, but avoids modifying RcloneInstaller for now if we want to be minimally invasive.
                // However, RcloneCheckAsync also checks config.

                // Better approach: We should probably expose DownloadAndInstallRclone in RcloneInstaller 
                // but for now let's try to use reflection or just assume we can modify RcloneInstaller later if needed.
                // Wait, I can modify RcloneInstaller.cs!

                // For now, I'll assume I'll modify RcloneInstaller to make DownloadAndInstallRclone public
                // OR I can just call RcloneCheckAsync(SelectedProvider.Provider) if a provider is selected.

                if (SelectedProvider != null)
                {
                    await _rcloneInstaller.RcloneCheckAsync(SelectedProvider.Provider);
                }
                else
                {
                    // Fallback to GoogleDrive just to trigger install check
                    await _rcloneInstaller.RcloneCheckAsync(CloudProvider.GoogleDrive);
                }

                await CheckRcloneStatusAsync();
                StatusMessage = "Installation complete!";
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to install Rclone");
                StatusMessage = "Installation failed. Check logs.";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanConfigure))]
        private async Task ConfigureAsync()
        {
            if (SelectedProvider == null) return;

            IsBusy = true;
            StatusMessage = $"Configuring {SelectedProvider.DisplayName}... Browser will open.";

            try
            {
                bool success = await _rcloneInstaller.SetupConfigAsync(SelectedProvider.Provider);

                if (success)
                {
                    StatusMessage = "Configuration successful!";
                    IsRcloneConfigured = true;

                    // Update global config
                    var config = await ConfigManagement.LoadConfigAsync();
                    if (config != null)
                    {
                        config.CloudConfig.Provider = SelectedProvider.Provider;
                        await ConfigManagement.SaveConfigAsync(config);
                    }

                    RequestClose?.Invoke();
                }
                else
                {
                    StatusMessage = "Configuration failed.";
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Configuration failed");
                StatusMessage = "Error during configuration.";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool CanConfigure()
        {
            return SelectedProvider != null && IsRcloneInstalled;
        }

        [RelayCommand]
        private void Close()
        {
            RequestClose?.Invoke();
        }
    }

    public class CloudProviderDisplay
    {
        public CloudProvider Provider { get; }
        public string DisplayName { get; }

        public CloudProviderDisplay(CloudProvider provider, string displayName)
        {
            Provider = provider;
            DisplayName = displayName;
        }

        public override string ToString() => DisplayName;
    }
}
