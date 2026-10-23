using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using MsBox.Avalonia.Windows;

namespace GDMENUCardManager
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);

            // Avalonia toggles a focused check box or radio button on Enter. Only Space should.
            InputElement.KeyDownEvent.AddClassHandler<ToggleButton>((button, e) =>
            {
                if (e.Key == Key.Enter)
                    e.Handled = true;
            }, RoutingStrategies.Tunnel);

            // MessageBox.Avalonia boxes with only an OK button ignore Escape. The others already map it to their cancel-type button.
            InputElement.KeyDownEvent.AddClassHandler<MsBoxWindow>((window, e) =>
            {
                if (e.Key == Key.Escape && !window.GetVisualDescendants().OfType<Button>().Any(b => b.IsCancel && b.IsEffectivelyVisible))
                {
                    e.Handled = true;
                    window.Close();
                }
            });
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = new MainWindow();

            base.OnFrameworkInitializationCompleted();
        }

        private void AboutMenuItem_Click(object sender, EventArgs e)
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                var about = new AboutWindow();
                _ = about.ShowDialog(desktop.MainWindow);
            }
        }
    }
}
