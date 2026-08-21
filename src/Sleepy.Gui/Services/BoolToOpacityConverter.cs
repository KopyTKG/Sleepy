using Microsoft.UI.Xaml.Data;

namespace Sleepy.Gui.Services;

/// <summary>
/// Dims a section while it is disabled. WinUI greys out controls on its own, but
/// labels and headings keep their colour, which reads as "available".
/// </summary>
public sealed class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is bool flag && flag ? 1.0 : 0.45;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
