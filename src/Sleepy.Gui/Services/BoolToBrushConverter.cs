using Microsoft.UI.Xaml.Data;

namespace Sleepy.Gui.Services;

/// <summary>Turns the status flag into the colour of the status dot.</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isError = value is bool flag && flag;
        var key = isError ? "ErrorBrush" : "SuccessBrush";
        return Application.Current.Resources[key];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
