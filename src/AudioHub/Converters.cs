using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AudioHub;

/// <summary>true → Visible. ConverterParameter="Invert" — наоборот. Не-bool: null → Collapsed.</summary>
public sealed class VisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value is bool flag ? flag : value is not null;
        if (parameter as string == "Invert") visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Значок кнопки mute: [громкость 0..100, mute, это микрофон?] → глиф Segoe Fluent Icons.</summary>
public sealed class VolumeGlyphConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var volume = values.Length > 0 && values[0] is double v ? v : 0;
        var muted = values.Length > 1 && values[1] is true;
        var isInput = values.Length > 2 && values[2] is true;
        return Glyphs.ForVolume(volume, muted, isInput);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public static class Glyphs
{
    public const string FontFamily = "Segoe Fluent Icons, Segoe MDL2 Assets";

    public const string Mute = "";
    public const string Microphone = "";
    public const string MicrophoneOff = "";
    public const string Volume0 = "";
    public const string Volume1 = "";
    public const string Volume2 = "";
    public const string Volume3 = "";

    public static string ForVolume(double volume, bool muted, bool isInput = false)
    {
        if (isInput) return muted ? MicrophoneOff : Microphone;
        if (muted) return Mute;
        return volume switch
        {
            <= 0 => Volume0,
            < 34 => Volume1,
            < 67 => Volume2,
            _ => Volume3,
        };
    }
}
