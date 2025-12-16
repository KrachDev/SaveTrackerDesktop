using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveTracker.Resources.LOGIC;
using SaveTracker.Resources.HELPERS;
using MsBox.Avalonia;
using Avalonia.Controls;

namespace SaveTracker.ViewModels
{
    public partial class QuarantineViewModel : ObservableObject
    {
        private readonly Game _game;
        private readonly QuarantineManager _manager;

        [ObservableProperty]
        private ObservableCollection<QuarantinedItem> _items = new();

        [ObservableProperty]
        private QuarantinedItem? _selectedItem;

        public QuarantineViewModel(Game game)
        {
            _game = game;
            _manager = new QuarantineManager(_game.InstallDirectory);
            RefreshList();
        }

        private void RefreshList()
        {
            Items.Clear();
            var files = _manager.GetQuarantinedFiles();
            foreach (var f in files)
            {
                Items.Add(f);
            }
        }

        [RelayCommand]
        private async Task Restore()
        {
            if (SelectedItem == null) return;

            try
            {
                var box = MessageBoxManager.GetMessageBoxStandard(new MsBox.Avalonia.Dto.MessageBoxStandardParams
                {
                    ButtonDefinitions = MsBox.Avalonia.Enums.ButtonEnum.YesNo,
                    ContentTitle = "Confirm Restore",
                    ContentMessage = $"Are you sure you want to restore '{SelectedItem.FileName}'?\n\nTarget will be: {SelectedItem.OriginalPath}",
                    Icon = MsBox.Avalonia.Enums.Icon.Question
                });

                var result = await box.ShowAsync();
                if (result == MsBox.Avalonia.Enums.ButtonResult.Yes)
                {
                    _manager.RestoreFile(SelectedItem);
                    RefreshList();
                }
            }
            catch (Exception ex)
            {
                DebugConsole.WriteException(ex, "Failed to restore file");
            }
        }

        [RelayCommand]
        private async Task Delete()
        {
            if (SelectedItem == null) return;

            try
            {
                // Verify delete
                var box = MessageBoxManager.GetMessageBoxStandard(new MsBox.Avalonia.Dto.MessageBoxStandardParams
                {
                    ButtonDefinitions = MsBox.Avalonia.Enums.ButtonEnum.YesNo,
                    ContentTitle = "Delete permanently?",
                    ContentMessage = "This cannot be undone.",
                    Icon = MsBox.Avalonia.Enums.Icon.Warning
                });

                if (await box.ShowAsync() == MsBox.Avalonia.Enums.ButtonResult.Yes)
                {
                    if (System.IO.File.Exists(SelectedItem.FilePath))
                        System.IO.File.Delete(SelectedItem.FilePath);

                    // delete meta
                    string meta = SelectedItem.FilePath + ".meta.txt";
                    if (System.IO.File.Exists(meta)) System.IO.File.Delete(meta);

                    RefreshList();
                }
            }
            catch (Exception ex) { DebugConsole.WriteException(ex, "Failed to delete from quarantine"); }
        }
    }
}
