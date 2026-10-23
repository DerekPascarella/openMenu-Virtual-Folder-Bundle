using System.Windows;
using System.Windows.Input;

namespace GDMENUCardManager
{
    /// <summary>
    /// Interaction logic for CopyNameWindow.xaml
    /// </summary>
    public partial class CopyNameWindow : Window
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

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
