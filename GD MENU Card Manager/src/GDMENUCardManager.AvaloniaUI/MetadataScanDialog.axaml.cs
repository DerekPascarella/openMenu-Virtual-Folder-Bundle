using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System;

namespace GDMENUCardManager
{
    public partial class MetadataScanDialog : Window
    {
        public bool StartScan { get; private set; }

        public MetadataScanDialog()
        {
            InitializeComponent();
        }

        public MetadataScanDialog(int gameCount) : this()
        {
            var gameCountText = this.FindControl<TextBlock>("GameCountText");
            if (gameCountText != null)
                gameCountText.Text = gameCount.ToString();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            this.FindControl<Button>("StartScanButton")?.Focus(NavigationMethod.Tab);
        }

        private void QuitButton_Click(object sender, RoutedEventArgs e)
        {
            StartScan = false;
            Close();
        }

        private void StartScanButton_Click(object sender, RoutedEventArgs e)
        {
            StartScan = true;
            Close();
        }
    }
}
