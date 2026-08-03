using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace GDMENUCardManager.Converter
{
    // True only when every bound value is true.
    public class BoolAndMultiValueConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length == 0)
                return false;

            return values.All(v => v is bool b && b);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
