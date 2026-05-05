using System;
using System.Globalization;
using System.Windows.Data;
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
            throw new NotImplementedException();
        }
    }
}
