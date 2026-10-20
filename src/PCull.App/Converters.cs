using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
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
            var resourceKey = decision switch
            {
                "keep" => "SystemFillColorSuccessBrush",
                "review" => "SystemFillColorCautionBrush",
                "delete" => "SystemFillColorCriticalBrush",
                "favorite" => "SystemFillColorCautionBrush",
                _ => "SystemFillColorNeutralBrush"
            };
            var fallbackColor = decision switch
            {
                "keep" => Colors.MediumSeaGreen,
                "review" => Colors.DarkOrange,
                "delete" => Colors.IndianRed,
                "favorite" => Colors.Gold,
                _ => Colors.SlateGray
            };

            return ThemeBrushLookup.Get(resourceKey) ?? new SolidColorBrush(fallbackColor);
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
            var status = value?.ToString();
            var resourceKey = status switch
            {
                "Connected" => "SystemFillColorCriticalBrush",
                "Not matched" => "SystemFillColorSuccessBrush",
                "Skipped" => "AccentFillColorDefaultBrush",
                _ => "SystemFillColorNeutralBrush"
            };
            var fallbackColor = status switch
            {
                "Connected" => Colors.IndianRed,
                "Not matched" => Colors.MediumSeaGreen,
                "Skipped" => Colors.DodgerBlue,
                _ => Colors.Gray
            };

            return ThemeBrushLookup.Get(resourceKey) ?? new SolidColorBrush(fallbackColor);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }

    public sealed class DecisionGlyphConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value?.ToString() switch
            {
                "Keep" => "\uE73E",
                "Delete" => "\uE74D",
                _ => "\uE946"
            };

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }

    public sealed class StepStateBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is int currentStep && int.TryParse(parameter?.ToString(), out var step))
            {
                var resourceKey = currentStep == step
                    ? "AccentFillColorDefaultBrush"
                    : currentStep > step
                        ? "SystemFillColorSuccessBrush"
                        : "ControlFillColorSecondaryBrush";
                var brush = ThemeBrushLookup.Get(resourceKey);
                if (brush != null)
                    return brush;

                var fallback = currentStep == step
                    ? Colors.DodgerBlue
                    : currentStep > step
                        ? Colors.MediumSeaGreen
                        : Colors.Gray;
                return new SolidColorBrush(fallback);
            }

            return ThemeBrushLookup.Get("ControlFillColorSecondaryBrush") ?? new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }

    public sealed class StepStateForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is int currentStep && int.TryParse(parameter?.ToString(), out var step))
            {
                var key = currentStep >= step
                    ? "TextOnAccentFillColorPrimaryBrush"
                    : "TextFillColorSecondaryBrush";
                var brush = ThemeBrushLookup.Get(key);
                if (brush != null)
                    return brush;
                return new SolidColorBrush(currentStep >= step ? Colors.White : Colors.Gray);
            }

            return ThemeBrushLookup.Get("TextFillColorSecondaryBrush") ?? new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }

    public sealed class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }

    public sealed class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => value is bool flag && !flag ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }

    public sealed class DecisionOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => string.Equals(value?.ToString(), "Delete", StringComparison.OrdinalIgnoreCase) ? 0.55d : 1d;

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotSupportedException();
    }

    internal static class ThemeBrushLookup
    {
        public static Brush? Get(string key)
        {
            try
            {
                var application = Application.Current;
                return application != null && application.Resources.TryGetValue(key, out var resource)
                    ? resource as Brush
                    : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
