using Phrasor.Core.Llm;

namespace Phrasor.Core.Settings;

/// <summary>Stores API keys by provider id. The Windows app implements this with DPAPI.</summary>
public interface ISecretStore
{
    string? Get(string providerId);
    void Set(string providerId, string? value);
    void Clear();
}

public sealed class MissingKeyException(string providerName, string? keyUrl)
    : Exception($"Add your {providerName} API key in Settings → Model." + (keyUrl is null ? "" : $" Get a free one at {keyUrl}"))
{
    public string? KeyUrl { get; } = keyUrl;
}

public static class EndpointResolver
{
    /// <summary>
    /// The default model first, then the backups in order. <paramref name="activeModelId"/> is a temporary choice (the tray's model switch)
    /// and goes in front. Models without a needed key are skipped; if none can be used the first key problem is thrown.
    /// </summary>
    public static IReadOnlyList<LlmEndpoint> Resolve(AppSettings settings, ISecretStore secrets, string? activeModelId = null)
    {
        var list = new List<LlmEndpoint>();
        MissingKeyException? missing = null;

        foreach (var model in ModelList.Chain(settings, activeModelId))
        {
            var endpoint = Build(model.Provider, model.Model, model.BaseUrl, secrets, out var keyProblem, model.KeyId);
            if (endpoint is null)
            {
                missing ??= keyProblem;
                continue;
            }
            if (!list.Any(e => e.BaseUrl == endpoint.BaseUrl && e.Model == endpoint.Model))
                list.Add(endpoint);
        }

        if (list.Count == 0 && missing is not null) throw missing;
        return list;
    }

    /// <summary>
    /// The quality-check judge: the configured judge model when its key is saved, otherwise the
    /// optimiser's fallback (or main) model. Independent is false when the judge shares a provider
    /// with the optimiser, because then it grades its own model family.
    /// </summary>
    public static (LlmEndpoint Endpoint, bool Independent) ResolveJudge(AppSettings settings, ISecretStore secrets, IReadOnlyList<LlmEndpoint> optimiser)
    {
        if (optimiser.Count == 0) throw new ArgumentException("No optimiser endpoint.", nameof(optimiser));
        var cfg = settings.Judge;
        if (cfg is { Enabled: true } && !string.IsNullOrWhiteSpace(cfg.Model) &&
            Build(cfg.Provider, cfg.Model, cfg.BaseUrl, secrets, out _) is { } judge)
        {
            return (judge, !optimiser.Any(e => SameService(e, judge)));
        }
        return (optimiser.Count > 1 ? optimiser[1] : optimiser[0], false);
    }

    // Groq and Gemini are one service each whatever the URL; custom and local endpoints are told apart by URL.
    private static bool SameService(LlmEndpoint a, LlmEndpoint b) =>
        a.Preset.Id == b.Preset.Id &&
        (a.Preset.Id is not ("custom" or "ollama") || a.BaseUrl.TrimEnd('/').Equals(b.BaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));

    public static LlmEndpoint? ResolveVoice(AppSettings settings, ISecretStore secrets) =>
        Build(settings.Voice.Provider, settings.Voice.Model, settings.Voice.BaseUrl, secrets, out _);

    /// <param name="keyId">A key kept just for this model; null uses the provider's key.</param>
    public static LlmEndpoint? Build(string providerId, string model, string? baseUrl, ISecretStore secrets, out MissingKeyException? keyProblem, string? keyId = null)
    {
        keyProblem = null;
        var preset = ProviderPresets.Get(providerId);
        var url = string.IsNullOrWhiteSpace(baseUrl) || preset.Id is not ("custom" or "ollama") ? preset.BaseUrl : baseUrl.Trim();
        if (string.IsNullOrWhiteSpace(url)) return null;
        var key = secrets.Get(keyId ?? preset.Id);
        if (preset.RequiresKey && string.IsNullOrWhiteSpace(key))
        {
            keyProblem = new MissingKeyException(preset.DisplayName, preset.KeyUrl);
            return null;
        }
        return new LlmEndpoint(preset, url, model.Trim(), key);
    }
}
