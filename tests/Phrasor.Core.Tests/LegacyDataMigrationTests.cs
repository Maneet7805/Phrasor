using Phrasor.Core.Settings;

namespace Phrasor.Core.Tests;

public sealed class LegacyDataMigrationTests : IDisposable
{
    private static readonly string[] Ids = ["groq", "gemini", "custom"];
    private readonly string _root = Path.Combine(Path.GetTempPath(), "migr-" + Guid.NewGuid());
    private string Old => Path.Combine(_root, "PromptForge");
    private string New => Path.Combine(_root, "Phrasor");

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private void Put(string dir, string name, string text = "x")
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name), text);
    }

    private sealed class ThrowingSecrets : ISecretStore
    {
        public string? Get(string providerId) => throw new System.Security.Cryptography.CryptographicException("bad");
        public void Set(string providerId, string? value) { }
        public void Clear() { }
    }

    [Fact]
    public void Old_only_copies_settings_and_resaves_keys_and_leaves_the_old_folder()
    {
        Put(Old, "settings.json", "{\"a\":1}"); Put(Old, "secrets.dat");
        var oldS = new SettingsTests.MemorySecrets(); oldS.Set("groq", "gsk_fake");
        var newS = new SettingsTests.MemorySecrets();
        Assert.True(LegacyDataMigration.Run(Old, New, oldS, newS, Ids));
        Assert.Equal("gsk_fake", newS.Get("groq"));
        Assert.Equal("{\"a\":1}", File.ReadAllText(Path.Combine(New, "settings.json")));
        Assert.True(File.Exists(Path.Combine(Old, "settings.json")));
        Assert.True(File.Exists(Path.Combine(Old, "secrets.dat")));
    }

    [Fact]
    public void Both_present_is_left_alone()
    {
        Put(Old, "settings.json", "old"); Put(Old, "secrets.dat"); Put(New, "settings.json", "new");
        var oldS = new SettingsTests.MemorySecrets(); oldS.Set("groq", "gsk_fake");
        var newS = new SettingsTests.MemorySecrets();
        Assert.False(LegacyDataMigration.Run(Old, New, oldS, newS, Ids));
        Assert.Equal("new", File.ReadAllText(Path.Combine(New, "settings.json")));
        Assert.Null(newS.Get("groq"));
    }

    [Fact]
    public void Neither_present_does_nothing()
    {
        var newS = new SettingsTests.MemorySecrets();
        Assert.False(LegacyDataMigration.Run(Old, New, new SettingsTests.MemorySecrets(), newS, Ids));
        Assert.False(Directory.Exists(New));
    }

    [Fact]
    public void Unreadable_old_key_still_copies_settings_without_crashing()
    {
        Put(Old, "settings.json", "s"); Put(Old, "secrets.dat");
        var newS = new SettingsTests.MemorySecrets();
        Assert.True(LegacyDataMigration.Run(Old, New, new ThrowingSecrets(), newS, Ids));
        Assert.Null(newS.Get("groq"));
        Assert.True(File.Exists(Path.Combine(New, "settings.json")));
    }

    [Fact]
    public void Nothing_logged_contains_a_key_or_a_path()
    {
        Put(Old, "settings.json"); Put(Old, "secrets.dat");
        var oldS = new SettingsTests.MemorySecrets(); oldS.Set("groq", "gsk_fake");
        var log = new List<string>();
        LegacyDataMigration.Run(Old, New, oldS, new SettingsTests.MemorySecrets(), Ids, log.Add);
        Assert.NotEmpty(log);
        Assert.All(log, l => { Assert.DoesNotContain("gsk_fake", l); Assert.DoesNotContain(_root, l); Assert.DoesNotContain(Environment.UserName, l); });
    }
}
