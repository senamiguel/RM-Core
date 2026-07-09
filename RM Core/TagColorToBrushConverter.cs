using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RM_Core
{
    public class TagColorToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string color = value as string;
            return color switch
            {
                "green"  => new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)),
                "yellow" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 193, 7)),
                "red"    => new SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 67, 54)),
                "blue"   => new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243)),
                _        => new SolidColorBrush(System.Windows.Media.Color.FromArgb(0, 0, 0, 0)),
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
