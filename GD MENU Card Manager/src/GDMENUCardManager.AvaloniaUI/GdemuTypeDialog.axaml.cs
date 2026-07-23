using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace GDMENUCardManager
{
    public partial class GdemuTypeDialog : Window
    {
        public bool IsAuthentic { get; private set; }
        private bool _answered;

        public GdemuTypeDialog()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (!_answered)
                e.Cancel = true;
            base.OnClosing(e);
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            IsAuthentic = this.FindControl<RadioButton>("AuthenticRadio")?.IsChecked == true;
            _answered = true;
            Close();
        }
    }
}
