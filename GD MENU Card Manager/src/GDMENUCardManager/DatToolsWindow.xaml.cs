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

        #region Import Tab

        private void ChooseImportFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select DAT import folder";

                if (dialog.ShowDialog(new Win32Window(this)) == System.Windows.Forms.DialogResult.OK)
                {
                    var result = dialog.SelectedPath;

                    // Validate the folder contains at least one DAT file
                    var boxPath = Path.Combine(result, "BOX.DAT");
                    var metaPath = Path.Combine(result, "META.DAT");

                    if (!File.Exists(boxPath) && !File.Exists(metaPath))
                    {
                        MessageBox.Show(this, "Selected folder does not contain BOX.DAT or META.DAT.",
                            "Error", MessageBoxButton.OK, MessageBoxImage.None);
                        return;
                    }

                    _importSourcePath = result;
                    TextImportSourcePath.Text = result;
                    ButtonBeginImport.IsEnabled = true;
                }
            }
        }

        private async void BeginImport_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_importSourcePath))
                return;

            // Confirmation dialog
            var confirmResult = MessageBox.Show(this,
                "This will backup current DAT files and merge entries from the selected folder.\n\nContinue?",
                "Confirmation",
                MessageBoxButton.OKCancel,
                MessageBoxImage.None);

            if (confirmResult != MessageBoxResult.OK)
                return;

            bool overwriteExisting = RadioImportAll.IsChecked == true;

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
                    MessageBox.Show(this, result.errorMessage, "Error", MessageBoxButton.OK, MessageBoxImage.None);
                    return;
                }

                // Show success message first
                var message = $"Import completed successfully.\n\nBOX.DAT entries merged: {result.boxEntriesMerged}\nMETA.DAT entries merged: {result.metaEntriesMerged}";
                if (result.boxEntriesMerged > 0)
                {
                    message += "\n\nICON.DAT was automatically regenerated using the updated contents of BOX.DAT.";
                }
                MessageBox.Show(this, message, "Information", MessageBoxButton.OK, MessageBoxImage.None);

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
                MessageBox.Show(this, $"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.None);
            }
        }

        #endregion

        #region Export Tab

        private void ChooseExportFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select PNG export folder";

                if (dialog.ShowDialog(new Win32Window(this)) == System.Windows.Forms.DialogResult.OK)
                {
                    _exportTargetPath = dialog.SelectedPath;
                    TextExportTargetPath.Text = dialog.SelectedPath;
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
                    MessageBox.Show(this, result.errorMessage, "Error", MessageBoxButton.OK, MessageBoxImage.None);
                    return;
                }

                // Keep window open, just show success
                MessageBox.Show(this, $"Exported {result.exportedCount} artwork file(s) to PNG.",
                    "Information", MessageBoxButton.OK, MessageBoxImage.None);
            }
            catch (Exception ex)
            {
                progressWindow.AllowClose();
                progressWindow.Close();
                MessageBox.Show(this, $"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.None);
            }
        }

        #endregion

        #region Clear Tab

        private async void ClearDats_Click(object sender, RoutedEventArgs e)
        {
            // Confirmation dialog
            var confirmResult = MessageBox.Show(this,
                "This will backup current DAT files and then clear ALL artwork and metadata entries.\n\nThis action cannot be undone. Continue?",
                "Confirmation",
                MessageBoxButton.OKCancel,
                MessageBoxImage.None);

            if (confirmResult != MessageBoxResult.OK)
                return;

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
                    MessageBox.Show(this, result.errorMessage, "Error", MessageBoxButton.OK, MessageBoxImage.None);
                    return;
                }

                // Show success message first
                MessageBox.Show(this, "All DAT entries have been cleared.",
                    "Information", MessageBoxButton.OK, MessageBoxImage.None);

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
                MessageBox.Show(this, $"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.None);
            }
        }

        #endregion

        #region Overwrite Tab

        private async void OverwriteDats_Click(object sender, RoutedEventArgs e)
        {
            // Confirmation dialog
            var confirmResult = MessageBox.Show(this,
                "This will backup current DAT files and overwrite them with those from the SD card's openMenu disc image.\n\nContinue?",
                "Confirmation",
                MessageBoxButton.OKCancel,
                MessageBoxImage.None);

            if (confirmResult != MessageBoxResult.OK)
                return;

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
                    MessageBox.Show(this, result.errorMessage, "Error", MessageBoxButton.OK, MessageBoxImage.None);
                    return;
                }

                MessageBox.Show(this, "DAT files have been successfully overwritten with those from the SD card.",
                    "Information", MessageBoxButton.OK, MessageBoxImage.None);

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
                MessageBox.Show(this, $"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.None);
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
                if (result != true) return Task.FromResult(false); // user canceled
            }
        }
    }
}
