using Microsoft.Win32;

namespace Phrasor.App.Infrastructure;

/// <summary>Start-with-Windows via the per-user Run key (no admin rights, no scheduled task).</summary>
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Phrasor";

    // Under --data-dir a test copy must not touch the real Run key.
    public static bool IsEnabled() => !AppPaths.IsOverridden && IsEnabledFor(AppPaths.ExePath);

    public static bool IsEnabledFor(string exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string value && value.Contains(exePath, StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(bool enabled)
    {
        if (!AppPaths.IsOverridden) SetFor(AppPaths.ExePath, enabled);
    }

    public static void SetFor(string exePath, bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, $"\"{exePath}\" --background");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
