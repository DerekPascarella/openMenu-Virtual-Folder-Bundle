using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using GDMENUCardManager.Core;

namespace GDMENUCardManager
{
    public class SerialTranslationItem : INotifyPropertyChanged
    {
        public GdItem Item { get; set; }
        public string OriginalSerial { get; set; }
        public string TranslatedSerial { get; set; }
        public string GameName { get; set; }

        private bool _isChecked = true; // Default to keeping translation
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public partial class SerialTranslationDialog : Window
    {
        private List<SerialTranslationItem> _items;

        public SerialTranslationDialog()
        {
            InitializeComponent();

            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    Close();
                }
            };
        }

        public SerialTranslationDialog(IEnumerable<GdItem> translatedItems) : this()
        {
            _items = translatedItems.Select(item => new SerialTranslationItem
            {
                Item = item,
                OriginalSerial = item.OriginalSerial,
                TranslatedSerial = item.ProductNumber,
                GameName = item.Name ?? ""
            }).ToList();

            var listControl = this.FindControl<ItemsControl>("TranslationList");
            if (listControl != null)
                listControl.ItemsSource = _items;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            Dispatcher.UIThread.Post(() =>
            {
                var listControl = this.FindControl<ItemsControl>("TranslationList");
                listControl?.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault()?.Focus(NavigationMethod.Tab);
            }, DispatcherPriority.Loaded);
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Process each item based on checkbox state
            foreach (var translationItem in _items)
            {
                if (translationItem.IsChecked)
                {
                    // User accepted the translation, so clear the tracking flags.
                    translationItem.Item.AcknowledgeSerialTranslation();
                }
                else
                {
                    // User wants the original serial, so revert the translation.
                    translationItem.Item.RevertSerialTranslation();
                }
            }

            Close();
        }
    }
}
