// Converters/NullToBoolConverter.cs
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace StackUTIL.Converters
{
    /// <summary>
    /// Конвертер: null → false, не-null → true
    /// </summary>
    public class NullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value != null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Конвертер: null → true, не-null → false (инверсия)
    /// </summary>
    public class NullToBoolInvertedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value == null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}