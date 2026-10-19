using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PcMonitor.App;

public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? !b : DependencyProperty.UnsetValue;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is bool b ? !b : DependencyProperty.UnsetValue;
}

/// <summary>Radio button helper: checked when the bound double equals the parameter.</summary>
public sealed class EqualsConverter : IValueConverter
{
    private static double Parse(object parameter) => double.Parse(System.Convert.ToString(parameter, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d && Math.Abs(d - Parse(parameter)) < 1e-9;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Parse(parameter) : Binding.DoNothing;
}

public sealed class AllTrueToVisibility : IMultiValueConverter
{
    public static readonly AllTrueToVisibility Instance = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.All(v => v is true) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
