using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GDMENUCardManager.Core;
using System;
using System.Configuration;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GDMENUCardManager
{
    public class UpdateAvailableDialog : Window
    {
        public string LatestTag { get; private set; }
        public string LatestVersion { get; private set; }
        public bool UserWantsUpdate { get; private set; }

        public UpdateAvailableDialog()
        {
            InitializeComponent();
        }

        public UpdateAvailableDialog(string latestTag, string latestVersion)
        {
            InitializeComponent();
            LatestTag = latestTag;
            LatestVersion = latestVersion;
            this.FindControl<TextBlock>("VersionText").Text = latestVersion;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            this.FindControl<Button>("UpdateButton")?.Focus(NavigationMethod.Tab);
        }

        private void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            UserWantsUpdate = true;
            Close();
        }

        private void RemindButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            SaveSkippedVersion(LatestTag);
            Close();
        }

        private void Changelog_Click(object sender, RoutedEventArgs e)
        {
            var url = "https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle?tab=readme-ov-file#changelog";
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    Process.Start("xdg-open", url);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    Process.Start("open", url);
            }
            catch { }
        }

        internal static bool ShouldSkipVersion(string latestTag)
        {
            var skipped = ConfigurationManager.AppSettings["SkippedUpdateVersion"];
            return !string.IsNullOrWhiteSpace(skipped) && skipped == latestTag;
        }

        internal static void SaveSkippedVersion(string tag)
        {
            if (Core.Manager.ConfigReadOnly) return;
            try
            {
                var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                if (config.AppSettings.Settings["SkippedUpdateVersion"] != null)
                    config.AppSettings.Settings["SkippedUpdateVersion"].Value = tag;
                else
                    config.AppSettings.Settings.Add("SkippedUpdateVersion", tag);
                RetiredSettings.Remove(config);
                config.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection("appSettings");
            }
            catch { }
        }
    }
}
