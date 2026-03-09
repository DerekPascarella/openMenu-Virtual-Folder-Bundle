using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MessageBox.Avalonia;
using MessageBox.Avalonia.Models;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using GDMENUCardManager.Core;

namespace GDMENUCardManager
{
    public partial class DatToolsWindow : Window
    {
        private readonly Core.Manager _manager;
        private readonly Func<Task> _reloadCallback;

        private string _importSourcePath;
        private string _exportTargetPath;

        private const int MaxPathDisplayLength = 50;

        // UI controls
        private TextBlock TextImportInstructions;
        private TextBlock TextClearInstructions;
        private TextBlock TextOverwriteInstructions;
        private TextBlock TextImportSourcePath;
        private TextBlock TextExportTargetPath;
        private Button ButtonBeginImport;
        private Button ButtonBeginExport;
        private RadioButton RadioImportMissing;
        private RadioButton RadioImportAll;

        public DatToolsWindow()
        {
            InitializeComponent();
        }

        public DatToolsWindow(Core.Manager manager, Func<Task> reloadCallback)
        {
            InitializeComponent();

            _manager = manager;
            _reloadCallback = reloadCallback;

            // Get references to UI controls
            TextImportInstructions = this.FindControl<TextBlock>("TextImportInstructions");
            TextClearInstructions = this.FindControl<TextBlock>("TextClearInstructions");
            TextOverwriteInstructions = this.FindControl<TextBlock>("TextOverwriteInstructions");
            TextImportSourcePath = this.FindControl<TextBlock>("TextImportSourcePath");
            TextExportTargetPath = this.FindControl<TextBlock>("TextExportTargetPath");
            ButtonBeginImport = this.FindControl<Button>("ButtonBeginImport");
            ButtonBeginExport = this.FindControl<Button>("ButtonBeginExport");
            RadioImportMissing = this.FindControl<RadioButton>("RadioImportMissing");
            RadioImportAll = this.FindControl<RadioButton>("RadioImportAll");

            SetPlatformSpecificText();

            this.KeyUp += (s, e) => { if (e.Key == Avalonia.Input.Key.Escape) Close(); };
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void SetPlatformSpecificText()
        {
            bool isMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            bool isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

            if (!isMacOS && !isLinux)
                return; // Windows: keep XAML defaults

            string datFolder = isMacOS
                ? "\"~/Library/Application Support/GDMENUCardManager/menu_data\""
                : "\"tools/openMenu/menu_data\"";

            string backupFolder = isMacOS
                ? "\"~/Library/Application Support/GDMENUCardManager/dat_backups\""
                : "\"dat_backups\"";

            TextImportInstructions.Text =
                $"Before this process is initiated, the DATs that currently reside in {datFolder} will be backed up to the {backupFolder} folder.\n\n" +
                "Use \"Choose DAT Folder\" to select a folder containing either BOX.DAT or META.DAT (or both), as ICON.DAT will be automatically generated from BOX.DAT.\n\n" +
                "Decide if only entries missing from the current DATs should be imported, or if all entries should be imported, overwriting anything currently existing. Then, click \"Begin Import\" to perform this operation.\n\n" +
                "Please note that any unsaved artwork changes from this session will also be included.";

            TextClearInstructions.Text =
                "Doing so will discard any unsaved artwork changes from Games List, as well as remove all saved entries.\n\n" +
                $"Before this process is initiated, the DATs that currently reside in {datFolder} will be backed up to the {backupFolder} folder.\n\n" +
                "Click \"Clear DATs\" to perform this operation.";

            if (isMacOS)
            {
                TextOverwriteInstructions.Text =
                    "The DAT files in \"~/Library/Application Support/GDMENUCardManager/menu_data\" are used each time openMenu is rebuilt and saved to the GDEMU SD card.\n\n" +
                    "On macOS, these DAT files reside in Application Support and are never overwritten by app updates, so in most cases this operation is not needed. Your artwork and metadata persist automatically across GD MENU Card Manager updates.\n\n" +
                    "However, if you are setting up on a new Mac or have otherwise lost your Application Support data, this tool can restore your DAT files directly from your SD card's existing openMenu disc image.";
            }
            else // Linux
            {
                TextOverwriteInstructions.Text =
                    "The DAT files in the \"tools/openMenu/menu_data\" folder are used each time openMenu is rebuilt and saved to the GDEMU SD card.\n\n" +
                    "However, there are several scenarios where a user may wish to immediately overwrite those with the DAT files that were used to generate their SD card's openMenu. For example, a user upgrading from a previous version of openMenu Virtual Folder Bundle will likely already have custom artwork.\n\n" +
                    "While such users can easily copy the DAT files from their previous version of GD MENU Card Manager's \"tools/openMenu/menu_data\" folder into the current version's \"menu_data\" folder, the tool here can perform this automatically using the SD card itself as the source.";
            }
        }

        /// <summary>
        /// Truncate a path for display, adding "..." if too long.
        /// </summary>
        private string TruncatePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "[no folder selected]";

            if (path.Length <= MaxPathDisplayLength)
                return path;

            // Show beginning and end with ... in middle
            int halfLength = (MaxPathDisplayLength - 3) / 2;
            return path.Substring(0, halfLength) + "..." + path.Substring(path.Length - halfLength);
        }

        #region Import Tab

        private async void ChooseImportFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select DAT import folder"
            };

            var result = await dialog.ShowAsync(this);
            if (!string.IsNullOrEmpty(result))
            {
                // Validate the folder contains at least one DAT file
                var boxPath = Path.Combine(result, "BOX.DAT");
                var metaPath = Path.Combine(result, "META.DAT");

                if (!File.Exists(boxPath) && !File.Exists(metaPath))
                {
                    await ShowError("Invalid Folder", "Selected folder does not contain BOX.DAT or META.DAT.");
                    return;
                }

                _importSourcePath = result;
                TextImportSourcePath.Text = TruncatePath(result);
                TextImportSourcePath.Foreground = Avalonia.Media.Brushes.Black;
                ButtonBeginImport.IsEnabled = true;
            }
        }

        private async void BeginImport_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_importSourcePath))
                return;

            // Confirmation dialog
            var confirmResult = await MessageBoxManager.GetMessageBoxCustomWindow(new MessageBox.Avalonia.DTO.MessageBoxCustomParams
            {
                ContentTitle = "Confirm Import",
                ContentMessage = "This will backup current DAT files and merge entries from the selected folder.\n\nContinue?",
                Icon = MessageBox.Avalonia.Enums.Icon.Warning,
                ShowInCenter = true,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ButtonDefinitions = new ButtonDefinition[]
                {
                    new ButtonDefinition { Name = "Continue" },
                    new ButtonDefinition { Name = "Cancel" }
                }
            }).ShowDialog(this);

            if (confirmResult != "Continue")
                return;

            bool overwriteExisting = RadioImportAll?.IsChecked == true;

            // Check DAT files are writable before proceeding
            if (!await EnsureDatFilesWritableWithDialog())
                return;

            // Show progress window
            var progressWindow = new ProgressWindow();
            progressWindow.Title = "Importing DAT Entries";
            progressWindow.TextContent = "Importing...";
            progressWindow.TotalItems = 100;
            progressWindow.ProcessedItems = 0;

            _ = progressWindow.ShowDialog(this);

            try
            {
                var result = await Task.Run(() =>
                {
                    return _manager.ImportDatEntries(_importSourcePath, overwriteExisting, progress =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            progressWindow.ProcessedItems = (int)(progress * 100);
                        });
                    });
                });

                progressWindow.AllowClose();
                progressWindow.Close();

                if (!result.success)
                {
                    await ShowError("Import Failed", result.errorMessage);
                    return;
                }

                // Show success message first
                var message = $"Import completed successfully.\n\nBOX.DAT entries merged: {result.boxEntriesMerged}\nMETA.DAT entries merged: {result.metaEntriesMerged}";
                if (result.boxEntriesMerged > 0)
                {
                    message += "\n\nICON.DAT was automatically regenerated using the updated contents of BOX.DAT.";
                }
                await ShowInfo("Import Complete", message);

                // Close this window and reload
                this.Close();

                if (_reloadCallback != null)
                {
                    await _reloadCallback();
                }
            }
            catch (Exception ex)
            {
                progressWindow.AllowClose();
                progressWindow.Close();
                await ShowError("Import Failed", $"An error occurred: {ex.Message}");
            }
        }

        #endregion

        #region Export Tab

        private async void ChooseExportFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select PNG export folder"
            };

            var result = await dialog.ShowAsync(this);
            if (!string.IsNullOrEmpty(result))
            {
                _exportTargetPath = result;
                TextExportTargetPath.Text = TruncatePath(result);
                TextExportTargetPath.Foreground = Avalonia.Media.Brushes.Black;
                ButtonBeginExport.IsEnabled = true;
            }
        }

        private async void BeginExport_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_exportTargetPath))
                return;

            // Show progress window
            var progressWindow = new ProgressWindow();
            progressWindow.Title = "Exporting Artwork";
            progressWindow.TextContent = "Exporting...";
            progressWindow.TotalItems = 100;
            progressWindow.ProcessedItems = 0;

            _ = progressWindow.ShowDialog(this);

            try
            {
                var result = await Task.Run(() =>
                {
                    return _manager.ExportArtworkToPngs(_exportTargetPath, progress =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            progressWindow.ProcessedItems = (int)(progress * 100);
                        });
                    });
                });

                progressWindow.AllowClose();
                progressWindow.Close();

                if (!result.success)
                {
                    await ShowError("Export Failed", result.errorMessage);
                    return;
                }

                // Keep window open, just show success
                await ShowInfo("Export Complete", $"Exported {result.exportedCount} artwork file(s) to PNG.");
            }
            catch (Exception ex)
            {
                progressWindow.AllowClose();
                progressWindow.Close();
                await ShowError("Export Failed", $"An error occurred: {ex.Message}");
            }
        }

        #endregion

        #region Clear Tab

        private async void ClearDats_Click(object sender, RoutedEventArgs e)
        {
            // Confirmation dialog
            var confirmResult = await MessageBoxManager.GetMessageBoxCustomWindow(new MessageBox.Avalonia.DTO.MessageBoxCustomParams
            {
                ContentTitle = "Confirm Clear",
                ContentMessage = "This will backup current DAT files and then clear ALL artwork and metadata entries.\n\nThis action cannot be undone. Continue?",
                Icon = MessageBox.Avalonia.Enums.Icon.Warning,
                ShowInCenter = true,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ButtonDefinitions = new ButtonDefinition[]
                {
                    new ButtonDefinition { Name = "Clear All" },
                    new ButtonDefinition { Name = "Cancel" }
                }
            }).ShowDialog(this);

            if (confirmResult != "Clear All")
                return;

            // Check DAT files are writable before proceeding
            if (!await EnsureDatFilesWritableWithDialog())
                return;

            // Show progress window
            var progressWindow = new ProgressWindow();
            progressWindow.Title = "Clearing DAT Files";
            progressWindow.TextContent = "Clearing...";
            progressWindow.TotalItems = 100;
            progressWindow.ProcessedItems = 50; // Show some progress for indeterminate

            _ = progressWindow.ShowDialog(this);

            try
            {
                var result = await Task.Run(() => _manager.ClearAllDatEntries());

                progressWindow.AllowClose();
                progressWindow.Close();

                if (!result.success)
                {
                    await ShowError("Clear Failed", result.errorMessage);
                    return;
                }

                // Show success message first
                await ShowInfo("Clear Complete", "All DAT entries have been cleared.");

                // Close this window and reload
                this.Close();

                if (_reloadCallback != null)
                {
                    await _reloadCallback();
                }
            }
            catch (Exception ex)
            {
                progressWindow.AllowClose();
                progressWindow.Close();
                await ShowError("Clear Failed", $"An error occurred: {ex.Message}");
            }
        }

        #endregion

        #region Overwrite Tab

        private async void OverwriteDats_Click(object sender, RoutedEventArgs e)
        {
            // Confirmation dialog
            var confirmResult = await MessageBoxManager.GetMessageBoxCustomWindow(new MessageBox.Avalonia.DTO.MessageBoxCustomParams
            {
                ContentTitle = "Confirm Overwrite",
                ContentMessage = "This will backup current DAT files and overwrite them with those from the SD card's openMenu disc image.\n\nContinue?",
                Icon = MessageBox.Avalonia.Enums.Icon.Warning,
                ShowInCenter = true,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ButtonDefinitions = new ButtonDefinition[]
                {
                    new ButtonDefinition { Name = "Continue" },
                    new ButtonDefinition { Name = "Cancel" }
                }
            }).ShowDialog(this);

            if (confirmResult != "Continue")
                return;

            // Check DAT files are writable before proceeding
            if (!await EnsureDatFilesWritableWithDialog())
                return;

            // Show progress window
            var progressWindow = new ProgressWindow();
            progressWindow.Title = "Overwriting DAT Files";
            progressWindow.TextContent = "Extracting DATs from SD card...";
            progressWindow.TotalItems = 100;
            progressWindow.ProcessedItems = 50;

            _ = progressWindow.ShowDialog(this);

            try
            {
                var result = await Task.Run(() => _manager.OverwriteDatsFromSdCard());

                progressWindow.AllowClose();
                progressWindow.Close();

                if (!result.success)
                {
                    await ShowError("Overwrite Failed", result.errorMessage);
                    return;
                }

                await ShowInfo("Overwrite Complete", "DAT files have been successfully overwritten with those from the SD card.");

                // Close this window and reload
                this.Close();

                if (_reloadCallback != null)
                {
                    await _reloadCallback();
                }
            }
            catch (Exception ex)
            {
                progressWindow.AllowClose();
                progressWindow.Close();
                await ShowError("Overwrite Failed", $"An error occurred: {ex.Message}");
            }
        }

        #endregion

        #region Helpers

        private async Task ShowError(string title, string message)
        {
            await MessageBoxManager.GetMessageBoxStandardWindow(title, message,
                MessageBox.Avalonia.Enums.ButtonEnum.Ok, MessageBox.Avalonia.Enums.Icon.Error)
                .ShowDialog(this);
        }

        private async Task ShowInfo(string title, string message)
        {
            await MessageBoxManager.GetMessageBoxStandardWindow(title, message,
                MessageBox.Avalonia.Enums.ButtonEnum.Ok, MessageBox.Avalonia.Enums.Icon.Info)
                .ShowDialog(this);
        }

        /// <summary>
        /// Checks DAT file writability with a retry dialog owned by this window
        /// (not MainWindow) so it appears on top of DatToolsWindow.
        /// </summary>
        private async Task<bool> EnsureDatFilesWritableWithDialog()
        {
            while (true)
            {
                var lockedFiles = _manager.CheckDatFilesAccessibility();
                if (lockedFiles.Count == 0) return true;

                var dialog = new LockedFilesDialog(lockedFiles);
                await dialog.ShowDialog(this);
                if (!dialog.Result) return false; // user cancelled
            }
        }

        #endregion
    }
}
