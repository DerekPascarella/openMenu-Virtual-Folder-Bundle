using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace GDMENUCardManager.Converter
{
    public class DropTargetBrushConverter : IValueConverter
    {
        private static readonly IBrush Highlight = new SolidColorBrush(Color.Parse("#ADD8E6"));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool isTarget && isTarget ? Highlight : Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
