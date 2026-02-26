using System;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using GDMENUCardManager.Core;

namespace GDMENUCardManager
{
    public partial class UpdateWizardWindow : Window
    {
        private readonly string _tag;
        private CancellationTokenSource _cts;
        private bool _downloadComplete;
        private bool _installing;

        public UpdateWizardWindow(string tag, string version)
        {
            InitializeComponent();
            _tag = tag;
            StatusText.Text = $"Downloading update {version}...";

            this.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape && !_installing)
                    CancelAndClose();
            };

            this.Closing += OnWindowClosing;
        }

        private void OnWindowClosing(object sender, CancelEventArgs e)
        {
            if (_installing)
            {
                e.Cancel = true;
                return;
            }

            _cts?.Cancel();
            if (!_downloadComplete)
                UpdateManager.CleanupStagingDirectory();
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            Page1.Visibility = Visibility.Collapsed;
            Page2.Visibility = Visibility.Visible;
            StartDownload();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void CancelDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            CancelAndClose();
        }

        private void CancelAndClose()
        {
            _cts?.Cancel();
            UpdateManager.CleanupStagingDirectory();
            Close();
        }

        private async void StartDownload()
        {
            _cts = new CancellationTokenSource();
            var progress = new Progress<DownloadProgress>(p =>
            {
                if (p.TotalBytes > 0)
                {
                    var pct = (double)p.BytesRead / p.TotalBytes * 100;
                    DownloadProgress.Value = pct;
                    SizeText.Text = $"{FormatBytes(p.BytesRead)} / {FormatBytes(p.TotalBytes)}";
                }
                else
                {
                    DownloadProgress.IsIndeterminate = true;
                    SizeText.Text = $"{FormatBytes(p.BytesRead)} downloaded";
                }
                SpeedText.Text = $"Download speed: {FormatSpeed(p.SpeedBytesPerSecond)}";
            });

            try
            {
                // Download
                await UpdateManager.DownloadUpdateAsync(_tag, progress, _cts.Token);

                // Extract
                StatusText.Text = "Extracting update...";
                DownloadProgress.IsIndeterminate = true;
                SpeedText.Text = "";
                SizeText.Text = "";
                await UpdateManager.ExtractUpdateAsync(_tag, _cts.Token);

                // Apply preservation
                StatusText.Text = "Applying preservation options...";
                var options = new PreservationOptions
                {
                    PreserveDats = ChkPreserveDats.IsChecked == true,
                    PreserveThemes = ChkPreserveThemes.IsChecked == true,
                    PreserveCheats = ChkPreserveCheats.IsChecked == true,
                    PreserveSettings = ChkPreserveSettings.IsChecked == true
                };
                await UpdateManager.ApplyPreservationOptionsAsync(options);

                // Ready to install
                _downloadComplete = true;
                StatusText.Text = "Update ready to install.\n\nThe application will close and relaunch automatically.";
                DownloadProgress.IsIndeterminate = false;
                DownloadProgress.Value = 100;
                SpeedText.Text = "";
                SizeText.Text = "";
                CancelDownloadButton.Content = "Cancel";
                InstallButton.Visibility = Visibility.Visible;
            }
            catch (OperationCanceledException)
            {
                // User cancelled - window is closing
            }
            catch (Exception ex)
            {
                UpdateManager.CleanupStagingDirectory();
                MessageBox.Show(this, $"Update failed: {ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            _installing = true;
            UpdateManager.LaunchUpdaterAndExit();
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:0.#} MB";
            return $"{bytes / 1024.0 / 1024.0 / 1024.0:0.##} GB";
        }

        private static string FormatSpeed(double bytesPerSecond)
        {
            if (bytesPerSecond < 1024) return $"{bytesPerSecond:0} B/s";
            if (bytesPerSecond < 1024 * 1024) return $"{bytesPerSecond / 1024.0:0.#} KB/s";
            return $"{bytesPerSecond / 1024.0 / 1024.0:0.#} MB/s";
        }
    }
}
