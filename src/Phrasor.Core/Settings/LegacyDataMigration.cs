namespace Phrasor.Core.Settings;

/// <summary>
/// One-time copy of the pre-rename PromptForge data (settings and API keys) into the Phrasor folder.
/// The keys are read through <paramref name="oldSecrets"/> (old folder, old encryption entropy) and written through
/// <paramref name="newSecrets"/>, so the Windows encryption stays in the App. The old folder is never changed.
/// </summary>
public static class LegacyDataMigration
{
    private const string SettingsName = "settings.json";
    private const string SecretsName = "secrets.dat";

    /// <summary>
    /// Runs only when the new folder has neither settings.json nor secrets.dat and the old one has at least one.
    /// Never throws and logs only counts and error types: no keys, no paths.
    /// </summary>
    public static bool Run(string oldDir, string newDir, ISecretStore oldSecrets, ISecretStore newSecrets,
        IEnumerable<string> providerIds, Action<string>? log = null)
    {
        try
        {
            var oldSettings = Path.Combine(oldDir, SettingsName);
            var oldKeys = Path.Combine(oldDir, SecretsName);
            var hasOldSettings = File.Exists(oldSettings);
            var hasOldKeys = File.Exists(oldKeys);
            if (!hasOldSettings && !hasOldKeys) return false;
            if (File.Exists(Path.Combine(newDir, SettingsName)) || File.Exists(Path.Combine(newDir, SecretsName))) return false;

            var keys = 0;
            if (hasOldKeys)
            {
                foreach (var id in providerIds)
                {
                    string? key;
                    try { key = oldSecrets.Get(id); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
                    {
                        log?.Invoke($"old key could not be read ({ex.GetType().Name}); continuing without it");
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    newSecrets.Set(id, key);
                    keys++;
                }
            }

            // Settings go last: once settings.json exists the migration never runs again.
            if (hasOldSettings)
            {
                Directory.CreateDirectory(newDir);
                var temp = Path.Combine(newDir, SettingsName + ".tmp");
                File.Copy(oldSettings, temp, overwrite: true);
                File.Move(temp, Path.Combine(newDir, SettingsName));
            }

            log?.Invoke($"migrated PromptForge data: settings {(hasOldSettings ? "copied" : "absent")}, {keys} key(s) re-saved");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"PromptForge data migration failed ({ex.GetType().Name}); starting fresh");
            return false;
        }
    }
}
