using Microsoft.UI.Xaml.Data;

namespace Sleepy.Gui.Services;

/// <summary>Flips a bool, for the "enabled while not busy" bindings.</summary>
public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is not bool flag || !flag;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is not bool flag || !flag;
}
