using Avalonia.Controls;
using SaveTracker.Resources.Logic.RecloneManagement;
using SaveTracker.ViewModels;
using SaveTracker.Views.Dialog;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SaveTracker.Helpers
{
    /// <summary>
    /// Helper class to manage App ID input dialog workflow
    /// </summary>
    public static class AppIdInputHelper
    {
        /// <summary>
        /// Shows the App ID entry dialog for games that need manual App ID input
        /// </summary>
        public static async Task ShowAppIdInputDialogAsync(Window? parent, List<string> gameNames)
        {
            if (gameNames == null || gameNames.Count == 0)
                return;

            var viewModel = new AppIdEntryViewModel(gameNames.ToArray());
            var window = new AppIdEntryWindow(viewModel);

            // Show dialog and wait for result
            var result = await window.ShowDialog<bool?>(parent);

            if (result == true)
            {
                // User confirmed - save the App IDs
                var cacheService = CloudLibraryCacheService.Instance;
                foreach (var game in viewModel.Games)
                {
                    if (!string.IsNullOrEmpty(game.AppId))
                    {
                        await cacheService.SetManualAppId(game.GameName, game.AppId);
                    }
                }
            }
        }
    }
}
