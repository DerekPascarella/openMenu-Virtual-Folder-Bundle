using Avalonia.Data.Converters;
using System;
using System.Globalization;
using GDMENUCardManager.Core;

namespace GDMENUCardManager.Converter
{
    public class IsGameDiscConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is GdItem item)
                return item.DiscType == "Game";
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Avalonia.Data.BindingNotification.UnsetValue;
        }
    }
}
