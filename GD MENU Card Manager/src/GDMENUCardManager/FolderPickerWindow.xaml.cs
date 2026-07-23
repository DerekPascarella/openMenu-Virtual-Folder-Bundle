using System.Collections.Generic;
using System.Windows;

namespace GDMENUCardManager
{
    public partial class FolderPickerWindow : Window
    {
        public string SelectedPath { get; private set; }

        public FolderPickerWindow(List<string> paths)
        {
            InitializeComponent();
            FolderList.ItemsSource = paths;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedPath = FolderList.SelectedItem as string;
            DialogResult = SelectedPath != null;
        }

        private void FolderList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (FolderList.SelectedItem is string selected)
            {
                SelectedPath = selected;
                DialogResult = true;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
