using Phrasor.Core.Install;

namespace Phrasor.App.Infrastructure;

internal static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Phrasor");
    /// <summary>Pre-rename data folder; read once by the migration, never written.</summary>
    public static string LegacyDataDir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PromptForge");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string SecretsFile => Path.Combine(DataDir, "secrets.dat");
    public static string LogDir => Path.Combine(DataDir, "logs");
    public static string HistoryDir { get; } = InstallPaths.HistoryDir(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    public static string HistoryFile => Path.Combine(HistoryDir, "history.dat");
    public static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;
}
