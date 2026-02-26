using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using GDMENUCardManager.Core;

namespace GDMENUCardManager
{
    public partial class ManualUpdateDialog : Window
    {
        public string LatestTag { get; private set; }

        public ManualUpdateDialog(string latestTag, string latestVersion, ManualUpdateReason reason)
        {
            InitializeComponent();
            LatestTag = latestTag;

            if (reason == ManualUpdateReason.UnsupportedPlatform)
                ReasonText.Text = $"A new version of GD MENU Card Manager ({latestVersion}) is available. Auto-update is not supported on this platform.";
            else
                ReasonText.Text = $"A new version of GD MENU Card Manager ({latestVersion}) is available, but this release cannot be auto-updated.";

            this.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                    Close();
            };
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateAvailableDialog.SaveSkippedVersion(LatestTag);
            Close();
        }

        private void ReleasesLink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo("cmd", $"/c start {e.Uri.AbsoluteUri}") { CreateNoWindow = true });
            e.Handled = true;
        }
    }
}
