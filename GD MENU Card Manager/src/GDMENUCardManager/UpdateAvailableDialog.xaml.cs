using System.Configuration;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using GDMENUCardManager.Core;

namespace GDMENUCardManager
{
    public partial class UpdateAvailableDialog : Window
    {
        public string LatestTag { get; private set; }
        public string LatestVersion { get; private set; }
        public bool UserWantsUpdate { get; private set; }

        public UpdateAvailableDialog(string latestTag, string latestVersion)
        {
            InitializeComponent();
            LatestTag = latestTag;
            LatestVersion = latestVersion;
            VersionText.Text = latestVersion;

            this.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                    Close();
            };
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

        private void Changelog_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo("cmd", $"/c start {e.Uri.AbsoluteUri}") { CreateNoWindow = true });
            e.Handled = true;
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
                config.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection("appSettings");
            }
            catch { }
        }
    }
}
