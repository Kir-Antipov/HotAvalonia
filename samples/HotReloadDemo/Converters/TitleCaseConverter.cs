using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace HotReloadDemo.Converters;

/// <summary>
/// Represents a converter that converts the first character of a string to its uppercase equivalent.
/// </summary>
public sealed class TitleCaseConverter : IValueConverter
{
    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || !targetType.IsAssignableFrom(typeof(string)))
            return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);

        return string.IsNullOrEmpty(text) ? text : (char.ToUpper(text[0]) + text.Substring(1));
    }

    /// <inheritdoc/>
    object? IValueConverter.ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
