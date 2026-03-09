using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System.Collections.Generic;

namespace GDMENUCardManager
{
    public partial class ConfigReadOnlyDialog : Window
    {
        public class LockedFileInfo
        {
            public string Path { get; set; }
            public string Error { get; set; }
        }

        public bool Result { get; private set; }

        public ConfigReadOnlyDialog()
        {
            InitializeComponent();
        }

        public ConfigReadOnlyDialog(string configPath, string error) : this()
        {
            var items = new List<LockedFileInfo>
            {
                new LockedFileInfo { Path = configPath, Error = error }
            };

            var listBox = this.FindControl<ListBox>("FileListBox");
            if (listBox != null)
                listBox.Items = items;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            Close();
        }

        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            Close();
        }
    }
}
