using Phrasor.Core.Llm;

namespace Phrasor.Core.Settings;

/// <summary>
/// Keeps the saved-model list, the default and the backup chain consistent with the older Primary and Fallback fields,
/// and answers questions about them. No Windows APIs.
/// </summary>
public static class ModelList
{
    public const int MaxSavedModels = 10;
    private const string KeyPrefix = "model:";

    /// <summary>Where a model's own key lives in the key store. Provider keys use the plain provider id.</summary>
    public static string KeyIdFor(string modelId) => KeyPrefix + modelId;

    public static bool IsKeyIdOfModel(string keyId) => keyId.StartsWith(KeyPrefix, StringComparison.Ordinal) && IsValidId(keyId[KeyPrefix.Length..]);

    public static bool IsValidId(string? id) => id is { Length: >= 1 and <= 12 } && id.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9'));

    /// <summary>A model that runs on this PC: Ollama, or a custom address that points at this machine. It says nothing about what that server does next.</summary>
    public static bool IsLocal(string provider, string? baseUrl)
    {
        if (provider.Equals(ProviderPresets.Ollama.Id, StringComparison.OrdinalIgnoreCase)) return true;
        if (!provider.Equals(ProviderPresets.Custom.Id, StringComparison.OrdinalIgnoreCase)) return false;
        if (!Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var uri)) return false;
        return uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Makes the list, the default and the backups valid and in step with <see cref="AppSettings.Primary"/> and
    /// <see cref="AppSettings.Fallback"/>. When the two disagree, Primary and Fallback win: an older window or a hand edit changed them.
    /// Safe to call again and again.
    /// </summary>
    public static void Normalise(AppSettings s)
    {
        s.Primary ??= new EndpointSettings();
        s.Fallback ??= new EndpointSettings { Enabled = false };
        var list = s.SavedModels ??= [];
        s.BackupModelIds ??= [];

        list.RemoveAll(m => m is null || string.IsNullOrWhiteSpace(m.Model));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in list)
        {
            m.Provider = ProviderPresets.Get(m.Provider).Id;
            m.Model = m.Model.Trim();
            m.BaseUrl = Url(m.BaseUrl);
            if (!IsValidId(m.Id) || !seen.Add(m.Id)) { m.Id = NewId(seen); seen.Add(m.Id); }
        }
        if (list.Count > MaxSavedModels) list.RemoveRange(MaxSavedModels, list.Count - MaxSavedModels);

        // Default model: follows Primary.
        var primaryModel = string.IsNullOrWhiteSpace(s.Primary.Model) ? ProviderPresets.Get(s.Primary.Provider).DefaultModel : s.Primary.Model.Trim();
        var def = list.FirstOrDefault(m => m.Id == s.DefaultModelId);
        if (def is null) def = Find(list, s.Primary.Provider, primaryModel, s.Primary.BaseUrl) ?? Add(list, s.Primary.Provider, primaryModel, s.Primary.BaseUrl, seen);
        else if (!Same(def, s.Primary.Provider, primaryModel, s.Primary.BaseUrl))
        {
            def.Provider = ProviderPresets.Get(s.Primary.Provider).Id;
            def.Model = primaryModel;
            def.BaseUrl = Url(s.Primary.BaseUrl);
        }
        s.DefaultModelId = def.Id;

        // Backups: the first one follows Fallback.
        var chain = s.BackupModelIds.Where(id => id != def.Id && list.Any(m => m.Id == id)).Distinct().ToList();
        if (!s.Fallback.Enabled) chain.Clear();
        else if (!string.IsNullOrWhiteSpace(s.Fallback.Model))
        {
            var fb = s.Fallback;
            var fbModel = fb.Model.Trim();
            var firstMatches = chain.Count > 0 && Same(list.First(m => m.Id == chain[0]), fb.Provider, fbModel, fb.BaseUrl);
            if (!firstMatches && !Same(def, fb.Provider, fbModel, fb.BaseUrl))
            {
                var entry = Find(list, fb.Provider, fbModel, fb.BaseUrl, except: def.Id) ?? Add(list, fb.Provider, fbModel, fb.BaseUrl, seen);
                chain.Remove(entry.Id);
                chain.Insert(0, entry.Id);
            }
        }
        s.BackupModelIds = chain;

        // Mirrors.
        s.Primary.Provider = def.Provider; s.Primary.Model = def.Model; s.Primary.BaseUrl = def.BaseUrl; s.Primary.KeyId = def.KeyId;
        if (chain.Count > 0)
        {
            var b = list.First(m => m.Id == chain[0]);
            s.Fallback.Provider = b.Provider; s.Fallback.Model = b.Model; s.Fallback.BaseUrl = b.BaseUrl; s.Fallback.KeyId = b.KeyId; s.Fallback.Enabled = true;
        }
        else s.Fallback.Enabled = false;
    }

    /// <summary>The models to try, in order: the temporary choice if any, the default, then the backups. Unknown ids are ignored.</summary>
    public static IReadOnlyList<SavedModel> Chain(AppSettings s, string? activeModelId = null)
    {
        Normalise(s);
        var ids = new List<string>();
        if (activeModelId is not null) ids.Add(activeModelId);
        ids.Add(s.DefaultModelId);
        ids.AddRange(s.BackupModelIds);
        return [.. ids.Distinct().Select(id => s.SavedModels.FirstOrDefault(m => m.Id == id)).OfType<SavedModel>()];
    }

    /// <summary>Key-store ids that belonged to a model's own key and no longer do: the model was removed or went back to the provider's key.</summary>
    public static IReadOnlyList<string> OrphanedKeyIds(AppSettings before, AppSettings after) =>
    [
        .. before.SavedModels.Where(m => m.SeparateKey)
            .Where(m => !after.SavedModels.Any(n => n.Id == m.Id && n.SeparateKey))
            .Select(m => KeyIdFor(m.Id)),
    ];

    private static SavedModel? Find(List<SavedModel> list, string provider, string model, string? url, string? except = null) =>
        list.FirstOrDefault(m => m.Id != except && Same(m, provider, model, url));

    private static bool Same(SavedModel m, string provider, string model, string? url) =>
        m.Provider == ProviderPresets.Get(provider).Id && m.Model == model && string.Equals(m.BaseUrl, Url(url), StringComparison.OrdinalIgnoreCase);

    private static string? Url(string? url) => string.IsNullOrWhiteSpace(url) ? null : url.Trim();

    private static SavedModel Add(List<SavedModel> list, string provider, string model, string? url, HashSet<string> seen)
    {
        if (list.Count >= MaxSavedModels) list.RemoveAt(list.Count - 1);
        var m = new SavedModel { Id = NewId(seen), Provider = ProviderPresets.Get(provider).Id, Model = model, BaseUrl = Url(url) };
        seen.Add(m.Id);
        list.Add(m);
        return m;
    }

    private static string NewId(HashSet<string> taken)
    {
        string id;
        do id = "m" + Guid.NewGuid().ToString("N")[..8];
        while (taken.Contains(id));
        return id;
    }
}
