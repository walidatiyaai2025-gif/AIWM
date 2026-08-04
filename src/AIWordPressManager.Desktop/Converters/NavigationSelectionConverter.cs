using System.Globalization;
using System.Windows.Data;

namespace AIWordPressManager.Desktop.Converters;

public sealed class NavigationSelectionConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
            return false;

        var destination = values[0]?.ToString();
        var currentPage = values[1]?.ToString();
        return !string.IsNullOrWhiteSpace(destination)
               && string.Equals(destination, currentPage, StringComparison.OrdinalIgnoreCase);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
