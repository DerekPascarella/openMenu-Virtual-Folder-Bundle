using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System;

namespace GDMENUCardManager
{
    public partial class DiscDbMigrationDialog : Window
    {
        public bool Proceed { get; private set; }

        public DiscDbMigrationDialog()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            this.FindControl<Button>("ProceedButton")?.Focus(NavigationMethod.Tab);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Proceed = false;
            Close();
        }

        private void ProceedButton_Click(object sender, RoutedEventArgs e)
        {
            Proceed = true;
            Close();
        }
    }
}
