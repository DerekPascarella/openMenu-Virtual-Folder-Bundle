using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace GDMENUCardManager
{
    public partial class TextWindow : Window
    {
        public string Text { get; set; }

        public TextWindow()
        {
            InitializeComponent();
        }

        public TextWindow(string title, string text) : this()
        {
            Title = title;
            Text = text;
            DataContext = this;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
