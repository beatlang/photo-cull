using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using System;

namespace PCull.App
{
    public sealed class ScoreConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is float score)
                return Math.Clamp(score * 10d, 0d, 100d);
            if (value is double doubleScore)
                return Math.Clamp(doubleScore * 10d, 0d, 100d);
            return 0d;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }

    public sealed class DecisionBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var decision = value?.ToString()?.ToLowerInvariant();
            var color = decision switch
            {
                "keep" => Colors.MediumSeaGreen,
                "review" => Colors.DarkOrange,
                "delete" => Colors.IndianRed,
                "favorite" => Colors.Gold,
                _ => Colors.SlateGray
            };
            return new SolidColorBrush(color);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }

    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value is bool flag && flag ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => value is Visibility visibility && visibility == Visibility.Visible;
    }

    public sealed class MatchStatusBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var color = value?.ToString() switch
            {
                "Connected" => Colors.IndianRed,
                "Not matched" => Colors.MediumSeaGreen,
                "Skipped" => Colors.DodgerBlue,
                _ => Colors.Gray
            };
            return new SolidColorBrush(color);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }
}
