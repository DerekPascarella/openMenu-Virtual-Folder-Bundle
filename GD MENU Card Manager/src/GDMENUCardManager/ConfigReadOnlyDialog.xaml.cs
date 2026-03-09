using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace GDMENUCardManager
{
    public partial class ConfigReadOnlyDialog : Window
    {
        public class LockedFileInfo
        {
            public string Path { get; set; }
            public string Error { get; set; }
        }

        public ConfigReadOnlyDialog(string configPath, string error)
        {
            InitializeComponent();

            var items = new List<LockedFileInfo>
            {
                new LockedFileInfo { Path = configPath, Error = error }
            };

            FileListBox.ItemsSource = items;
        }

        private void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
