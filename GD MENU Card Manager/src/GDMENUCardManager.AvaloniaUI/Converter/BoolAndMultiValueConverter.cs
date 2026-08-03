using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GDMENUCardManager.Converter
{
    // True only when every bound value is true.
    public class BoolAndMultiValueConverter : IMultiValueConverter
    {
        public object Convert(IList<object> values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Count == 0)
                return false;

            return values.All(v => v is bool b && b);
        }
    }
}
