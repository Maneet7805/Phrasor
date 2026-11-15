namespace Phrasor.App.Infrastructure;

internal static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Phrasor");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string SecretsFile => Path.Combine(DataDir, "secrets.dat");
    public static string LogDir => Path.Combine(DataDir, "logs");
    public static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;
}
