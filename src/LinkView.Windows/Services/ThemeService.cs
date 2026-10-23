using System.Windows;
using System.Windows.Media;
using LinkView.Core.Settings;

namespace LinkView.Windows.Services;

/// <summary>统一管理主题、强调色与字号缩放，避免页面各自定义颜色。</summary>
public sealed class ThemeService
{
    private ResourceDictionary? _themeDictionary;
    private ResourceDictionary? _overrideDictionary;

    public bool IsDark { get; private set; }
    public event EventHandler? ThemeApplied;

    public void Initialize(AppearanceSettings appearance)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        EnsureBaseDictionaries(app);
        Apply(appearance);
    }

    private static void EnsureBaseDictionaries(Application app)
    {
        var sources = app.Resources.MergedDictionaries
            .Select(d => d.Source?.OriginalString ?? string.Empty)
            .ToArray();
        if (!sources.Any(s => s.Contains("Tokens.xaml", StringComparison.OrdinalIgnoreCase)))
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("Themes/Tokens.xaml", UriKind.Relative)
            });
        }

        if (!sources.Any(s => s.Contains("Controls.xaml", StringComparison.OrdinalIgnoreCase)))
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("Themes/Controls.xaml", UriKind.Relative)
            });
        }
    }

    public void Apply(AppearanceSettings appearance)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        IsDark = appearance.Theme == Core.Settings.ThemeMode.Dark ||
                 appearance.Theme == Core.Settings.ThemeMode.System && IsSystemDark();

        if (_themeDictionary is not null)
        {
            app.Resources.MergedDictionaries.Remove(_themeDictionary);
        }

        _themeDictionary = new ResourceDictionary
        {
            Source = new Uri(IsDark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative)
        };
        app.Resources.MergedDictionaries.Add(_themeDictionary);

        if (_overrideDictionary is not null)
        {
            app.Resources.MergedDictionaries.Remove(_overrideDictionary);
        }

        _overrideDictionary = BuildOverrides(appearance, IsDark);
        app.Resources.MergedDictionaries.Add(_overrideDictionary);

        ThemeApplied?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static ResourceDictionary BuildOverrides(AppearanceSettings appearance, bool dark)
    {
        var dictionary = new ResourceDictionary();
        var accent = ParseColor(appearance.AccentColor, Color.FromRgb(0x4C, 0x9A, 0xFF));
        var hover = Adjust(accent, dark ? 0.16 : -0.08);
        var pressed = Adjust(accent, dark ? -0.10 : -0.18);
        var onAccent = Luminance(accent) > 0.55 ? Color.FromRgb(0x10, 0x16, 0x1E) : Colors.White;

        dictionary["Brush.Accent"] = Freeze(new SolidColorBrush(accent));
        dictionary["Brush.AccentHover"] = Freeze(new SolidColorBrush(hover));
        dictionary["Brush.AccentPressed"] = Freeze(new SolidColorBrush(pressed));
        dictionary["Brush.AccentSoft"] = Freeze(new SolidColorBrush(Color.FromArgb(dark ? (byte)0x2A : (byte)0x1C,
            accent.R, accent.G, accent.B)));
        dictionary["Brush.AccentBorder"] = Freeze(new SolidColorBrush(Color.FromArgb(dark ? (byte)0x70 : (byte)0x50,
            accent.R, accent.G, accent.B)));
        dictionary["Brush.TextOnAccent"] = Freeze(new SolidColorBrush(onAccent));
        dictionary["Color.Accent"] = accent;

        var scale = Math.Clamp(appearance.FontScale, 0.8, 1.6);
        dictionary["FontSize.Caption"] = 11 * scale;
        dictionary["FontSize.Small"] = 12 * scale;
        dictionary["FontSize.Body"] = 13 * scale;
        dictionary["FontSize.Medium"] = 14 * scale;
        dictionary["FontSize.Subtitle"] = 16 * scale;
        dictionary["FontSize.Title"] = 21 * scale;
        dictionary["FontSize.Display"] = 28 * scale;
        dictionary["FontSize.Hero"] = 40 * scale;
        dictionary["FontSize.Code"] = 30 * scale;
        dictionary["Motion.Enabled"] = appearance.Animations == AnimationLevel.Full;
        dictionary["Motion.Reduced"] = appearance.Animations != AnimationLevel.Full;
        return dictionary;
    }

    private static Color ParseColor(string value, Color fallback)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(value);
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    private static Color Adjust(Color color, double factor)
    {
        static byte Clamp(double v) => (byte)Math.Clamp(v, 0, 255);
        if (factor >= 0)
        {
            return Color.FromRgb(
                Clamp(color.R + (255 - color.R) * factor),
                Clamp(color.G + (255 - color.G) * factor),
                Clamp(color.B + (255 - color.B) * factor));
        }

        return Color.FromRgb(
            Clamp(color.R * (1 + factor)),
            Clamp(color.G * (1 + factor)),
            Clamp(color.B * (1 + factor)));
    }

    private static double Luminance(Color color) =>
        (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255.0;

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
