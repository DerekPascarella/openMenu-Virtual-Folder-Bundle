using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MessageBox.Avalonia;
using MessageBox.Avalonia.Enums;
using System;
using System.ComponentModel;
using System.Threading;
using GDMENUCardManager.Core;

namespace GDMENUCardManager
{
    public class UpdateWizardWindow : Window
    {
        private readonly string _tag;
        private CancellationTokenSource _cts;
        private bool _downloadComplete;
        private bool _installing;

        private StackPanel Page1;
        private StackPanel Page2;
        private TextBlock StatusText;
        private ProgressBar DownloadProgress;
        private TextBlock SpeedText;
        private TextBlock SizeText;
        private Button CancelDownloadButton;
        private Button InstallButton;
        private CheckBox ChkPreserveDats;
        private CheckBox ChkPreserveThemes;
        private CheckBox ChkPreserveCheats;
        private CheckBox ChkPreserveSettings;

        public UpdateWizardWindow()
        {
            InitializeComponent();
            FindControls();
        }

        public UpdateWizardWindow(string tag, string version)
        {
            InitializeComponent();
            FindControls();
            _tag = tag;
            StatusText.Text = $"Downloading update {version}...";

            this.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape && !_installing)
                    CancelAndClose();
            };
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void FindControls()
        {
            Page1 = this.FindControl<StackPanel>("Page1");
            Page2 = this.FindControl<StackPanel>("Page2");
            StatusText = this.FindControl<TextBlock>("StatusText");
            DownloadProgress = this.FindControl<ProgressBar>("DownloadProgress");
            SpeedText = this.FindControl<TextBlock>("SpeedText");
            SizeText = this.FindControl<TextBlock>("SizeText");
            CancelDownloadButton = this.FindControl<Button>("CancelDownloadButton");
            InstallButton = this.FindControl<Button>("InstallButton");
            ChkPreserveDats = this.FindControl<CheckBox>("ChkPreserveDats");
            ChkPreserveThemes = this.FindControl<CheckBox>("ChkPreserveThemes");
            ChkPreserveCheats = this.FindControl<CheckBox>("ChkPreserveCheats");
            ChkPreserveSettings = this.FindControl<CheckBox>("ChkPreserveSettings");
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_installing)
            {
                e.Cancel = true;
                return;
            }

            _cts?.Cancel();
            if (!_downloadComplete)
                UpdateManager.CleanupStagingDirectory();

            base.OnClosing(e);
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            Page1.IsVisible = false;
            Page2.IsVisible = true;
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
                InstallButton.IsVisible = true;
            }
            catch (OperationCanceledException)
            {
                // User cancelled - window is closing
            }
            catch (Exception ex)
            {
                UpdateManager.CleanupStagingDirectory();
                var msgBox = MessageBoxManager.GetMessageBoxStandardWindow("Update Error",
                    $"Update failed: {ex.Message}", ButtonEnum.Ok, MessageBox.Avalonia.Enums.Icon.Error);
                await msgBox.ShowDialog(this);
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
