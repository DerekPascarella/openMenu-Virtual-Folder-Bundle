using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GDMENUCardManager.Core;

namespace GDMENUCardManager
{
    public partial class ArchiveAddModeDialog : Window
    {
        public ArchiveAddMode Result { get; private set; } = ArchiveAddMode.Cancel;

        public ArchiveAddModeDialog()
        {
            InitializeComponent();
        }

        public ArchiveAddModeDialog(int archiveCount)
            : this()
        {
            this.FindControl<TextBlock>("CountText").Text =
                $"A bulk add of {archiveCount} compressed disc images has been detected.";
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            this.FindControl<Button>("ParseNowButton")?.Focus();
        }

        private void ParseNowButton_Click(object sender, RoutedEventArgs e)
        {
            Result = ArchiveAddMode.ParseNow;
            Close();
        }

        private void DeferToSaveButton_Click(object sender, RoutedEventArgs e)
        {
            Result = ArchiveAddMode.DeferToSave;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Result = ArchiveAddMode.Cancel;
            Close();
        }
    }
}
