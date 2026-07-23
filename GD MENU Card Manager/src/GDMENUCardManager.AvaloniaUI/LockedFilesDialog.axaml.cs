using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System.Collections.Generic;
using System.Linq;

namespace GDMENUCardManager
{
    public partial class LockedFilesDialog : Window
    {
        public class LockedFileInfo
        {
            public string Path { get; set; }
            public string Error { get; set; }
        }

        public bool Result { get; private set; }

        public LockedFilesDialog()
        {
            InitializeComponent();
        }

        public LockedFilesDialog(Dictionary<string, string> lockedFiles) : this()
        {
            var items = lockedFiles.Select(kvp => new LockedFileInfo
            {
                Path = kvp.Key,
                Error = kvp.Value
            }).ToList();

            var listBox = this.FindControl<ListBox>("FileListBox");
            if (listBox != null)
                listBox.ItemsSource = items;
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

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            Close();
        }
    }
}
