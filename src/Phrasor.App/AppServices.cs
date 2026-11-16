using Phrasor.App.Infrastructure;
using Phrasor.Core.History;
using Phrasor.Core.Llm;
using Phrasor.Core.Optimization;
using Phrasor.Core.Settings;

namespace Phrasor.App;

/// <summary>Shared services for the tray app. One instance for the app's lifetime.</summary>
internal sealed class AppServices : IDisposable
{
    public AppServices()
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        LegacyDataMigration.Run(AppPaths.LegacyDataDir, AppPaths.DataDir,
            new DpapiSecretStore(Path.Combine(AppPaths.LegacyDataDir, "secrets.dat"), "PromptForge.v1.api-keys"),
            new DpapiSecretStore(AppPaths.SecretsFile), ProviderPresets.All.Select(p => p.Id), Log.Info);
        Settings = SettingsStore.Load(AppPaths.SettingsFile, Log.Warn);
        Secrets = new DpapiSecretStore(AppPaths.SecretsFile);
        Http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan }; // per-request timeouts are applied by the client
        Client = new OpenAiCompatibleClient(Http);
        Optimizer = new PromptOptimizer(Client);
        _history = new Lazy<HistoryStore>(() => new HistoryStore(new EncryptedHistoryFile(AppPaths.HistoryFile, new DpapiBlobProtector())));
    }

    public AppSettings Settings { get; set; }
    public ISecretStore Secrets { get; }
    public HttpClient Http { get; }
    public ILlmClient Client { get; }
    public PromptOptimizer Optimizer { get; }

    private readonly Lazy<HistoryStore> _history;

    /// <summary>Created on first use, so startup never touches the History file.</summary>
    public HistoryStore History => _history.Value;

    /// <summary>Shows a tray notification. Set by the tray context.</summary>
    public Action<string, ToolTipIcon> Notify { get; set; } = (_, _) => { };

    public OptimizerOptions OptimizerOptions => new()
    {
        Timeout = TimeSpan.FromSeconds(Math.Clamp(Settings.TimeoutSeconds, 10, 180)),
        RepairDroppedDetails = Settings.RepairDroppedDetails,
    };

    public void SaveSettings() => SettingsStore.Save(AppPaths.SettingsFile, Settings);

    public void Dispose() => Http.Dispose();
}
