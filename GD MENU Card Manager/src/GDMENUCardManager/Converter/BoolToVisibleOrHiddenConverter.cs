using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GDMENUCardManager.Converter
{
    // Hidden rather than Collapsed, so the layout slot is preserved.
    class BoolToVisibleOrHiddenConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return Binding.DoNothing;

            bool boolValue = (bool)value;
            bool inverse = parameter != null && parameter.ToString() == "Inverse";

            if (inverse)
                boolValue = !boolValue;

            return boolValue ? Visibility.Visible : Visibility.Hidden;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
