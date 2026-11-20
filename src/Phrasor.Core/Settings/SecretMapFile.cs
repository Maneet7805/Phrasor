using System.Text.Json;

namespace Phrasor.Core.Settings;

/// <summary>Reads the key file (provider id to encrypted key) that <c>DpapiSecretStore</c> keeps. Core so a damaged file can be tested on any OS.</summary>
public static class SecretMapFile
{
    /// <summary>A missing file gives an empty map. A damaged one is copied to <c>.bad</c> first, so a later save cannot silently drop the other keys.</summary>
    public static Dictionary<string, string> Read(string path, Action<string>? log = null)
    {
        if (!File.Exists(path)) return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [];
        }
        catch (JsonException ex)
        {
            log?.Invoke($"key file unreadable ({ex.GetType().Name}); kept a .bad copy");
            try { File.Copy(path, path + ".bad", overwrite: true); } catch (IOException) { }
            return [];
        }
    }
}
