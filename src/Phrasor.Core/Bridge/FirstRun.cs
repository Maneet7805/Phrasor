using Phrasor.Core.Llm;
using Phrasor.Core.Settings;

namespace Phrasor.Core.Bridge;

/// <summary>A provider and the key typed for it on the first-run page. The key is used once and never sent back.</summary>
public sealed record FirstRunKey(string Provider, string Key);

/// <summary>What the first-run page may set: a provider with a free key and a saved link, with that provider's default model.</summary>
public static class FirstRun
{
    /// <summary>Groq is suggested first. Other providers are set up in Settings.</summary>
    public static readonly IReadOnlyList<string> Providers = ["groq", "gemini"];

    /// <summary>Makes <paramref name="provider"/> the main model with its default model, and marks first run done.</summary>
    public static void Apply(AppSettings settings, string provider)
    {
        settings.Primary = new EndpointSettings { Provider = provider, Model = ProviderPresets.Get(provider).DefaultModel };
        settings.FirstRunComplete = true;
    }
}
