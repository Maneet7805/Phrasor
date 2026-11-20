using Phrasor.Core.Optimization;
using Phrasor.Core.Settings;
using Phrasor.Core.Theme;

namespace Phrasor.Core.Tests;

public class SettingsLoadTests
{
    [Fact]
    public void A_missing_settings_file_gives_defaults_and_no_bad_copy()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");

        var loaded = SettingsStore.Load(path);

        Assert.Equal(OptimizeMode.Standard, loaded.DefaultMode);
        Assert.False(File.Exists(path + ".bad"));
    }

    [Fact]
    public void A_settings_file_holding_only_null_gives_defaults()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "null");
            var loaded = SettingsStore.Load(path);
            Assert.Equal("groq", loaded.Primary.Provider);
            Assert.NotNull(loaded.Hotkeys);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Null_sections_in_the_settings_file_load_as_usable_defaults()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """{"primary":null,"fallback":null,"hotkeys":null,"voice":null,"standingRequirements":null}""");
            var loaded = SettingsStore.Load(path);
            Assert.NotNull(loaded.Primary);
            Assert.NotNull(loaded.Fallback);
            Assert.False(loaded.Fallback.Enabled);
            Assert.NotNull(loaded.Hotkeys);
            Assert.NotNull(loaded.Voice);
            Assert.Empty(loaded.StandingRequirements);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static AppSettings LoadJson(string json)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            return SettingsStore.Load(path);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".bad");
        }
    }

    [Fact]
    public void An_old_settings_file_without_ui_loads_with_the_system_theme_and_keeps_its_values()
    {
        var loaded = LoadJson("""{"version":1,"primary":{"provider":"gemini","model":"m"},"reviewBeforeReplace":false}""");

        Assert.Equal(ThemeMode.System, loaded.Ui.Theme);
        Assert.Equal("gemini", loaded.Primary.Provider);
        Assert.False(loaded.ReviewBeforeReplace);
    }

    [Fact]
    public void A_null_ui_section_loads_as_usable_defaults()
    {
        Assert.Equal(ThemeMode.System, LoadJson("""{"ui":null}""").Ui.Theme);
    }

    [Theory]
    [InlineData("Dark", ThemeMode.Dark)]
    [InlineData("light", ThemeMode.Light)]
    [InlineData("Sepia", ThemeMode.System)]
    [InlineData("7", ThemeMode.System)]
    public void The_theme_setting_reads_names_and_falls_back_to_system_for_unknown_values(string value, ThemeMode expected)
    {
        Assert.Equal(expected, LoadJson("{\"ui\":{\"theme\":\"" + value + "\"}}").Ui.Theme);
    }

    [Theory]
    [InlineData("""{"ui":{"theme":7}}""")]
    [InlineData("""{"ui":{"theme":{"a":[1]}}}""")]
    [InlineData("""{"ui":{"theme":null}}""")]
    public void A_theme_value_of_the_wrong_type_falls_back_to_system_and_the_rest_still_loads(string json)
    {
        var loaded = LoadJson(json.Replace("{\"ui\"", "{\"hotkeys\":{\"voice\":\"Ctrl+Alt+V\"},\"ui\""));

        Assert.Equal(ThemeMode.System, loaded.Ui.Theme);
        Assert.Equal("Ctrl+Alt+V", loaded.Hotkeys.Voice);
    }

    [Fact]
    public void The_theme_setting_round_trips()
    {
        var path = Path.GetTempFileName();
        try
        {
            SettingsStore.Save(path, new AppSettings { Ui = new UiSettings { Theme = ThemeMode.Dark } });
            Assert.Contains("\"Dark\"", File.ReadAllText(path));
            Assert.Equal(ThemeMode.Dark, SettingsStore.Load(path).Ui.Theme);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_failed_save_leaves_no_temp_file_and_a_later_save_removes_a_stale_one()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var path = Path.Combine(dir, "settings.json");
        Directory.CreateDirectory(path); // a folder where the file belongs makes the final move fail
        try
        {
            Assert.ThrowsAny<Exception>(() => SettingsStore.Save(path, new AppSettings()));
            Assert.False(File.Exists(path + ".tmp"));

            Directory.Delete(path);
            File.WriteAllText(path + ".tmp", "stale");
            SettingsStore.Save(path, new AppSettings());
            Assert.False(File.Exists(path + ".tmp"));
            Assert.True(File.Exists(path));
        }
        finally { Directory.Delete(dir, true); }
    }
}
