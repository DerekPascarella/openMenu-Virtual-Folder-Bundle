using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System.Collections.Generic;

namespace GDMENUCardManager
{
    public class FolderPickerWindow : Window
    {
        public List<string> Paths { get; }

        private ListBox FolderList => this.FindControl<ListBox>("FolderList");

        public FolderPickerWindow()
        {
            InitializeComponent();
        }

        public FolderPickerWindow(List<string> paths)
        {
            InitializeComponent();
            Paths = paths;
            DataContext = this;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Close(FolderList.SelectedItem as string);
        }

        private void FolderList_DoubleTapped(object sender, RoutedEventArgs e)
        {
            if (FolderList.SelectedItem is string selected)
                Close(selected);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close(null);
        }
    }
}
