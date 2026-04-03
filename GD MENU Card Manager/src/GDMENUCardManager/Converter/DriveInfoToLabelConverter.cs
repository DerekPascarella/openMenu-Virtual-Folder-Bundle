using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace GDMENUCardManager.Converter
{
    class DriveInfoToLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DriveInfo drive)
            {
                try
                {
                    if (drive.IsReady && !string.IsNullOrWhiteSpace(drive.VolumeLabel))
                        return $"{drive.Name} ({drive.VolumeLabel})";
                }
                catch { }
                return drive.Name;
            }
            return value?.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
