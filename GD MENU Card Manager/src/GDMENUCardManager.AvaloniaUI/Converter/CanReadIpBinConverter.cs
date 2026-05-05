using Avalonia.Data.Converters;
using System;
using System.Globalization;
using GDMENUCardManager.Core;

namespace GDMENUCardManager.Converter
{
    public class CanReadIpBinConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is FileFormat ff && ff != FileFormat.SevenZip;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Avalonia.Data.BindingNotification.UnsetValue;
        }
    }
}
