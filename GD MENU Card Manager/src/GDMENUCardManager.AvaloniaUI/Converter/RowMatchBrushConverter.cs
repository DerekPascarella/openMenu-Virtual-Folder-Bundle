using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace GDMENUCardManager.Converter
{
    public class RowMatchBrushConverter : IValueConverter
    {
        // Semi-transparent so the tint reads over both theme variants.
        private static readonly IBrush Highlight = new SolidColorBrush(Color.Parse("#40FFE066"));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool isMatch && isMatch ? Highlight : Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
