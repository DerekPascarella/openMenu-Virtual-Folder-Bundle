using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using GDMENUCardManager.Core;
using Avalonia.Interactivity;

namespace GDMENUCardManager
{
    public class CopyNameWindow : Window, INotifyPropertyChanged
    {
        public bool OnCard { get; set; }
        public bool NotOnCard { get; set; } = true;
        public bool FolderName { get; set; } = true;
        public bool ParseTosec { get; set; } = true;

        public CopyNameWindow()
        {
            InitializeComponent();
            DataContext = this;

            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    Close();
                }
            };
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            this.FindControl<CheckBox>("OnCardCheckBox")?.Focus(NavigationMethod.Tab);
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Close(true);
        }
    }
}
