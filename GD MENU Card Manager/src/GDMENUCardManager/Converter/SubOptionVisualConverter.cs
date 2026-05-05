using System;
using System.Globalization;
using System.Windows.Data;

namespace GDMENUCardManager.Converter
{
    public class SubOptionVisualConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length == 0)
                return false;
            if (!(values[0] is bool stored) || !stored)
                return false;
            if (values.Length == 1)
                return true;
            for (int i = 1; i < values.Length; i++)
                if (values[i] is bool b && b)
                    return true;
            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            var result = new object[targetTypes.Length];
            result[0] = value is bool b && b;
            for (int i = 1; i < targetTypes.Length; i++)
                result[i] = Binding.DoNothing;
            return result;
        }
    }
}
