namespace Phrasor.Core.Bridge;

/// <summary>The only links the page can open: fixed ids mapped to fixed https addresses. The page never sends a URL.</summary>
public static class LinkAllowList
{
    private static readonly Dictionary<string, Uri> Links = new(StringComparer.Ordinal)
    {
        ["groq-keys"] = new("https://console.groq.com/keys"),
        ["gemini-keys"] = new("https://aistudio.google.com/apikey"),
        ["project"] = new("https://github.com/Maneet7805/Phrasor"),
    };

    public static IReadOnlyCollection<string> Ids => Links.Keys;

    public static bool TryGet(string id, out Uri url) => Links.TryGetValue(id, out url!);

    public static bool IsAllowed(Uri url) => url.Scheme == Uri.UriSchemeHttps && Links.Values.Contains(url);
}
