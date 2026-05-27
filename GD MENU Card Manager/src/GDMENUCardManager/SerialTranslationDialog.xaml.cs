using System.Collections.Generic;
using System.Linq;
using System.Windows;
using GDMENUCardManager.Core;

namespace GDMENUCardManager
{
    public class SerialTranslationItem
    {
        public GdItem Item { get; set; }
        public string OriginalSerial { get; set; }
        public string TranslatedSerial { get; set; }
        public string GameName { get; set; }
        public bool IsChecked { get; set; } = true; // Default to keeping translation
    }

    public partial class SerialTranslationDialog : Window
    {
        private List<SerialTranslationItem> _items;

        public SerialTranslationDialog(IEnumerable<GdItem> translatedItems)
        {
            InitializeComponent();

            _items = translatedItems.Select(item => new SerialTranslationItem
            {
                Item = item,
                OriginalSerial = item.OriginalSerial,
                TranslatedSerial = item.ProductNumber,
                GameName = item.Name ?? ""
            }).ToList();

            TranslationList.ItemsSource = _items;
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

            DialogResult = true;
            Close();
        }
    }
}
