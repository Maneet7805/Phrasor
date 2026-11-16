using Phrasor.App.Infrastructure;
using Phrasor.Core.History;
using Phrasor.Core.Llm;
using Phrasor.Core.Optimization;
using Phrasor.Core.Settings;
using System.Security.Cryptography;

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

    /// <summary>How long saved items are kept: 0 (until deleted), 30 or 90 days.</summary>
    public int RetentionDays => HistoryRetention.Normalise(Settings.History.RetentionDays);

    /// <summary>
    /// Removes saved items past the retention. History stays lazy, so this runs when History is opened or saved to, not at startup:
    /// an old item can sit on disk until then. Nothing is read or written when retention is off.
    /// </summary>
    public void PruneHistory()
    {
        if (RetentionDays > 0) History.Prune(DateTime.UtcNow.AddDays(-RetentionDays));
    }

    /// <summary>The only way text reaches History: called when the user presses Save. Throws a store failure (see <see cref="IsHistoryFailure"/>).</summary>
    public void SaveToHistory(string original, string rewrite)
    {
        PruneHistory();
        History.Add(original, rewrite);
    }

    /// <summary>Same as <see cref="SaveToHistory"/> but returns false instead of throwing; logs the error type only.</summary>
    public bool TrySaveToHistory(string original, string rewrite)
    {
        try { SaveToHistory(original, rewrite); return true; }
        catch (Exception ex) when (IsHistoryFailure(ex))
        {
            Log.Warn($"history save failed type={ex.GetType().Name}");
            return false;
        }
    }

    /// <summary>Failures of the History file: unavailable (locked, damaged, from a newer Phrasor), disk, or not decryptable.</summary>
    public static bool IsHistoryFailure(Exception ex) => ex is InvalidOperationException or IOException or UnauthorizedAccessException or CryptographicException;

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
