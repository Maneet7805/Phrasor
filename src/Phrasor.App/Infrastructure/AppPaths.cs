using Phrasor.Core.Install;

namespace Phrasor.App.Infrastructure;

internal static class AppPaths
{
    /// <summary>Read from the command line here, not in Main, so no start-up order can use the real folders by mistake.</summary>
    public static DataRoot Root { get; } = DataRoot.Resolve(
        Environment.GetCommandLineArgs().Skip(1).ToArray(),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        InstallPaths.InstallDir(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
    public static bool IsOverridden => Root.IsOverridden;
    public static string? StartupError => Root.Error;
    public static string DataDir => Root.DataDir;
    /// <summary>Pre-rename data folder; read once by the migration, never written.</summary>
    public static string LegacyDataDir => Root.LegacyDataDir;
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string SecretsFile => Path.Combine(DataDir, "secrets.dat");
    public static string LogDir => Path.Combine(DataDir, "logs");
    public static string HistoryDir => Root.HistoryDir;
    public static string HistoryFile => Path.Combine(HistoryDir, "history.dat");
    public static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;
}
