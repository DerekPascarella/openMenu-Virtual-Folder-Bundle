using System;
using System.Globalization;
using System.Windows.Data;
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
            throw new NotImplementedException();
        }
    }
}
