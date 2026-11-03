using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Windows.UI.ViewManagement;

namespace AudioHub;

/// <summary>Цвета панели под тему Windows (светлая/тёмная) и системный акцентный цвет.</summary>
public static class Theme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Светлая ли тема у приложений (панель).</summary>
    public static bool AppsUseLight => ReadFlag("AppsUseLightTheme", fallback: true);

    /// <summary>Светлая ли панель задач (от неё зависит цвет значка в трее).</summary>
    public static bool TaskbarUsesLight => ReadFlag("SystemUsesLightTheme", fallback: false);

    public static void Apply(ResourceDictionary resources)
    {
        var light = AppsUseLight;
        Color accent;
        try
        {
            var settings = new UISettings();
            var value = settings.GetColorValue(light ? UIColorType.AccentDark1 : UIColorType.AccentLight2);
            accent = Color.FromArgb(value.A, value.R, value.G, value.B);
        }
        catch (Exception)
        {
            accent = light ? Color.FromRgb(0x00, 0x5F, 0xB8) : Color.FromRgb(0x60, 0xCD, 0xFF);
        }

        if (light)
        {
            SetBrush(resources, "Bg", "#F3F3F3");
            SetBrush(resources, "Card", "#FBFBFB");
            SetBrush(resources, "Hover", "#E9E9E9");
            SetBrush(resources, "Pressed", "#DFDFDF");
            SetBrush(resources, "Line", "#E0E0E0");
            SetBrush(resources, "Fg", "#1B1B1B");
            SetBrush(resources, "FgSecondary", "#616161");
            SetBrush(resources, "Rail", "#8A8A8A");
            SetBrush(resources, "ThumbOuter", "#FFFFFF");
        }
        else
        {
            SetBrush(resources, "Bg", "#2B2B2B");
            SetBrush(resources, "Card", "#323232");
            SetBrush(resources, "Hover", "#383838");
            SetBrush(resources, "Pressed", "#333333");
            SetBrush(resources, "Line", "#3D3D3D");
            SetBrush(resources, "Fg", "#FFFFFF");
            SetBrush(resources, "FgSecondary", "#A8A8A8");
            SetBrush(resources, "Rail", "#9A9A9A");
            SetBrush(resources, "ThumbOuter", "#454545");
        }

        resources["Accent"] = Frozen(new SolidColorBrush(accent));
        resources["AccentFg"] = Frozen(new SolidColorBrush(light ? Colors.White : Colors.Black));
    }

    private static void SetBrush(ResourceDictionary resources, string key, string hex) =>
        resources[key] = Frozen(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)));

    private static SolidColorBrush Frozen(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    private static bool ReadFlag(string name, bool fallback)
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue(name) is int value ? value != 0 : fallback;
    }
}
