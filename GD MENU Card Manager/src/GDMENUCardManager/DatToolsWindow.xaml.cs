using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
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

        public DatToolsWindow()
        {
            InitializeComponent();
        }

        public DatToolsWindow(Core.Manager manager, Func<Task> reloadCallback)
        {
            InitializeComponent();

            _manager = manager;
            _reloadCallback = reloadCallback;

            this.KeyUp += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
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

        private void ChooseImportFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select DAT import folder";

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var result = dialog.SelectedPath;

                    // Validate the folder contains at least one DAT file
                    var boxPath = Path.Combine(result, "BOX.DAT");
                    var metaPath = Path.Combine(result, "META.DAT");

                    if (!File.Exists(boxPath) && !File.Exists(metaPath))
                    {
                        MessageBox.Show("Selected folder does not contain BOX.DAT or META.DAT.",
                            "Invalid Folder", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    _importSourcePath = result;
                    TextImportSourcePath.Text = TruncatePath(result);
                    TextImportSourcePath.Foreground = Brushes.Black;
                    ButtonBeginImport.IsEnabled = true;
                }
            }
        }

        private async void BeginImport_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_importSourcePath))
                return;

            // Confirmation dialog
            var confirmResult = MessageBox.Show(
                "This will backup current DAT files and merge entries from the selected folder.\n\nContinue?",
                "Confirm Import",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (confirmResult != MessageBoxResult.OK)
                return;

            bool overwriteExisting = RadioImportAll.IsChecked == true;

            // Check DAT files are writable before proceeding
            if (!await EnsureDatFilesWritableWithDialog())
                return;

            // Show progress window
            var progressWindow = new ProgressWindow();
            progressWindow.Owner = this;
            progressWindow.Title = "Importing DAT Entries";
            progressWindow.TextContent = "Importing...";
            progressWindow.TotalItems = 100;
            progressWindow.ProcessedItems = 0;
            progressWindow.Show();

            try
            {
                var result = await Task.Run(() =>
                {
                    return _manager.ImportDatEntries(_importSourcePath, overwriteExisting, progress =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            progressWindow.ProcessedItems = (int)(progress * 100);
                        });
                    });
                });

                progressWindow.AllowClose();
                progressWindow.Close();

                if (!result.success)
                {
                    MessageBox.Show(result.errorMessage, "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Show success message first
                var message = $"Import completed successfully.\n\nBOX.DAT entries merged: {result.boxEntriesMerged}\nMETA.DAT entries merged: {result.metaEntriesMerged}";
                if (result.boxEntriesMerged > 0)
                {
                    message += "\n\nICON.DAT was automatically regenerated using the updated contents of BOX.DAT.";
                }
                MessageBox.Show(message, "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);

                // Close this window
                this.Close();

                // Reload
                if (_reloadCallback != null)
                {
                    await _reloadCallback();
                }
            }
            catch (Exception ex)
            {
                progressWindow.AllowClose();
                progressWindow.Close();
                MessageBox.Show($"An error occurred: {ex.Message}", "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Export Tab

        private void ChooseExportFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select PNG export folder";

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    _exportTargetPath = dialog.SelectedPath;
                    TextExportTargetPath.Text = TruncatePath(dialog.SelectedPath);
                    TextExportTargetPath.Foreground = Brushes.Black;
                    ButtonBeginExport.IsEnabled = true;
                }
            }
        }

        private async void BeginExport_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_exportTargetPath))
                return;

            // Show progress window
            var progressWindow = new ProgressWindow();
            progressWindow.Owner = this;
            progressWindow.Title = "Exporting Artwork";
            progressWindow.TextContent = "Exporting...";
            progressWindow.TotalItems = 100;
            progressWindow.ProcessedItems = 0;
            progressWindow.Show();

            try
            {
                var result = await Task.Run(() =>
                {
                    return _manager.ExportArtworkToPngs(_exportTargetPath, progress =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            progressWindow.ProcessedItems = (int)(progress * 100);
                        });
                    });
                });

                progressWindow.AllowClose();
                progressWindow.Close();

                if (!result.success)
                {
                    MessageBox.Show(result.errorMessage, "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Keep window open, just show success
                MessageBox.Show($"Exported {result.exportedCount} artwork file(s) to PNG.",
                    "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                progressWindow.AllowClose();
                progressWindow.Close();
                MessageBox.Show($"An error occurred: {ex.Message}", "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Clear Tab

        private async void ClearDats_Click(object sender, RoutedEventArgs e)
        {
            // Confirmation dialog
            var confirmResult = MessageBox.Show(
                "This will backup current DAT files and then clear ALL artwork and metadata entries.\n\nThis action cannot be undone. Continue?",
                "Confirm Clear",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (confirmResult != MessageBoxResult.OK)
                return;

            // Check DAT files are writable before proceeding
            if (!await EnsureDatFilesWritableWithDialog())
                return;

            // Show progress window
            var progressWindow = new ProgressWindow();
            progressWindow.Owner = this;
            progressWindow.Title = "Clearing DAT Files";
            progressWindow.TextContent = "Clearing...";
            progressWindow.TotalItems = 100;
            progressWindow.ProcessedItems = 50; // Show some progress for indeterminate
            progressWindow.Show();

            try
            {
                var result = await Task.Run(() => _manager.ClearAllDatEntries());

                progressWindow.AllowClose();
                progressWindow.Close();

                if (!result.success)
                {
                    MessageBox.Show(result.errorMessage, "Clear Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Show success message first
                MessageBox.Show("All DAT entries have been cleared.",
                    "Clear Complete", MessageBoxButton.OK, MessageBoxImage.Information);

                // Close this window
                this.Close();

                // Reload
                if (_reloadCallback != null)
                {
                    await _reloadCallback();
                }
            }
            catch (Exception ex)
            {
                progressWindow.AllowClose();
                progressWindow.Close();
                MessageBox.Show($"An error occurred: {ex.Message}", "Clear Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Overwrite Tab

        private async void OverwriteDats_Click(object sender, RoutedEventArgs e)
        {
            // Confirmation dialog
            var confirmResult = MessageBox.Show(
                "This will backup current DAT files and overwrite them with those from the SD card's openMenu disc image.\n\nContinue?",
                "Confirm Overwrite",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (confirmResult != MessageBoxResult.OK)
                return;

            // Check DAT files are writable before proceeding
            if (!await EnsureDatFilesWritableWithDialog())
                return;

            // Show progress window
            var progressWindow = new ProgressWindow();
            progressWindow.Owner = this;
            progressWindow.Title = "Overwriting DAT Files";
            progressWindow.TextContent = "Extracting DATs from SD card...";
            progressWindow.TotalItems = 100;
            progressWindow.ProcessedItems = 50;
            progressWindow.Show();

            try
            {
                var result = await Task.Run(() => _manager.OverwriteDatsFromSdCard());

                progressWindow.AllowClose();
                progressWindow.Close();

                if (!result.success)
                {
                    MessageBox.Show(result.errorMessage, "Overwrite Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                MessageBox.Show("DAT files have been successfully overwritten with those from the SD card.",
                    "Overwrite Complete", MessageBoxButton.OK, MessageBoxImage.Information);

                // Close this window
                this.Close();

                // Reload
                if (_reloadCallback != null)
                {
                    await _reloadCallback();
                }
            }
            catch (Exception ex)
            {
                progressWindow.AllowClose();
                progressWindow.Close();
                MessageBox.Show($"An error occurred: {ex.Message}", "Overwrite Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        /// <summary>
        /// Checks DAT file writability with a retry dialog owned by this window
        /// (not MainWindow) so it appears on top of DatToolsWindow.
        /// </summary>
        private Task<bool> EnsureDatFilesWritableWithDialog()
        {
            while (true)
            {
                var lockedFiles = _manager.CheckDatFilesAccessibility();
                if (lockedFiles.Count == 0) return Task.FromResult(true);

                var dialog = new LockedFilesDialog(lockedFiles) { Owner = this };
                var result = dialog.ShowDialog();
                if (result != true) return Task.FromResult(false); // user cancelled
            }
        }
    }
}
