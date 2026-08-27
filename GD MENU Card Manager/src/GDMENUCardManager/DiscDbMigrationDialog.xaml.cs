using System.Windows;

namespace GDMENUCardManager
{
    public partial class DiscDbMigrationDialog : Window
    {
        public bool Proceed { get; private set; }

        public DiscDbMigrationDialog()
        {
            InitializeComponent();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Proceed = false;
            DialogResult = false;
        }

        private void ProceedButton_Click(object sender, RoutedEventArgs e)
        {
            Proceed = true;
            DialogResult = true;
        }
    }
}
