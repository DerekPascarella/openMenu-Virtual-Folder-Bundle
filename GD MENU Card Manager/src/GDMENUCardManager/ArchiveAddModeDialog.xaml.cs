using System;
using System.Windows;
using GDMENUCardManager.Core;

namespace GDMENUCardManager
{
    public partial class ArchiveAddModeDialog : Window
    {
        public ArchiveAddMode Result { get; private set; } = ArchiveAddMode.Cancel;

        public ArchiveAddModeDialog(int archiveCount)
        {
            InitializeComponent();
            CountText.Text = $"A bulk add of {archiveCount} compressed disc images has been detected.";
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            ParseNowButton.Focus();
        }

        private void ParseNowButton_Click(object sender, RoutedEventArgs e)
        {
            Result = ArchiveAddMode.ParseNow;
            DialogResult = true;
        }

        private void DeferToSaveButton_Click(object sender, RoutedEventArgs e)
        {
            Result = ArchiveAddMode.DeferToSave;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Result = ArchiveAddMode.Cancel;
            DialogResult = false;
        }
    }
}
