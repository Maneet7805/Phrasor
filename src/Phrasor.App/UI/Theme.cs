using Microsoft.Win32;
using Phrasor.Core.Theme;

namespace Phrasor.App.UI;

/// <summary>
/// The app's current colours: the saved setting (System, Light or Dark) combined with the Windows light or dark choice.
/// Nothing calls this yet; windows move onto it in a later change.
/// </summary>
internal static class Theme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static ThemeMode _mode = ThemeMode.System;
    private static bool _watching;

    public static Palette Palette { get; private set; } = Palette.Dark;
    public static bool IsDark => ReferenceEquals(Palette, Palette.Dark);

    /// <summary>Raised when the palette in use changes, on the thread that detected it.</summary>
    public static event Action? Changed;

    /// <summary>Applies the saved setting and starts following Windows when the setting is System.</summary>
    public static void Apply(ThemeMode mode)
    {
        _mode = mode;
        if (!_watching)
        {
            SystemEvents.UserPreferenceChanged += (_, e) => { if (e.Category == UserPreferenceCategory.General) Refresh(); };
            _watching = true;
        }
        Refresh();
    }

    public static Color Color(string token) => System.Drawing.Color.FromArgb(0xFF, System.Drawing.Color.FromArgb(Palette[token]));

    private static void Refresh()
    {
        var next = _mode switch
        {
            ThemeMode.Light => Palette.Light,
            ThemeMode.Dark => Palette.Dark,
            _ => WindowsUsesLightApps() ? Palette.Light : Palette.Dark,
        };
        if (ReferenceEquals(next, Palette)) return;
        Palette = next;
        Changed?.Invoke();
    }

    private static bool WindowsUsesLightApps()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
    }
}
