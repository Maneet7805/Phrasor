using Phrasor.Core.Home;
using Phrasor.Core.Llm;
using Phrasor.Core.Settings;

namespace Phrasor.Core.Tests;

public class HomeStatusTests
{
    private static readonly IReadOnlySet<string> All = new HashSet<string> { "optimize", "selection", "voice" };

    private static HomeStatus Build(AppSettings s, string? key, bool paused = false, IReadOnlySet<string>? registered = null)
    {
        var secrets = new SettingsTests.MemorySecrets();
        if (key is not null) secrets.Set(s.Primary.Provider, key);
        return HomeStatus.Build(s, secrets, paused, registered ?? All);
    }

    [Fact]
    public void A_saved_key_and_registered_shortcuts_is_ready()
    {
        var h = Build(new AppSettings(), "fake-key");
        Assert.Equal(HomeState.Ready, h.State);
        Assert.True(h.KeySet);
        Assert.Equal("Groq (free tier)", h.Provider);
        Assert.Equal(3, h.Shortcuts.Count);
    }

    [Fact]
    public void No_key_says_so()
    {
        var h = Build(new AppSettings(), null);
        Assert.Equal(HomeState.NeedsKey, h.State);
        Assert.False(h.KeySet);
    }

    [Fact]
    public void A_blank_key_counts_as_no_key() => Assert.False(Build(new AppSettings(), "  ").KeySet);

    [Fact]
    public void A_local_model_needs_no_key()
    {
        var s = new AppSettings { Primary = new EndpointSettings { Provider = "ollama", Model = "llama3" } };
        var h = Build(s, null);
        Assert.False(h.KeyRequired);
        Assert.Equal(HomeState.Ready, h.State);
    }

    [Fact]
    public void Paused_wins_over_everything_else() => Assert.Equal(HomeState.Paused, Build(new AppSettings(), null, paused: true).State);

    [Fact]
    public void A_shortcut_windows_refused_is_a_problem_and_is_marked()
    {
        var h = Build(new AppSettings(), "fake-key", registered: new HashSet<string> { "optimize", "voice" });
        Assert.Equal(HomeState.ShortcutProblem, h.State);
        Assert.False(h.Shortcuts.Single(x => x.Id == "selection").Registered);
    }

    [Fact]
    public void Shortcuts_come_from_the_settings_and_the_voice_one_only_when_voice_is_on()
    {
        var s = new AppSettings { Hotkeys = new HotkeySettings { Optimize = "Ctrl+Alt+R" }, Voice = new VoiceSettings { Enabled = false } };
        var h = Build(s, "fake-key");
        Assert.Equal("Ctrl+Alt+R", h.Shortcuts.First(x => x.Id == "optimize").Chord);
        Assert.DoesNotContain(h.Shortcuts, x => x.Id == "voice");
    }

    [Fact]
    public void An_empty_shortcut_is_left_out_and_is_not_a_problem()
    {
        var s = new AppSettings { Hotkeys = new HotkeySettings { OptimizeSelection = "" } };
        var h = Build(s, "fake-key", registered: new HashSet<string> { "optimize", "voice" });
        Assert.Equal(HomeState.Ready, h.State);
        Assert.DoesNotContain(h.Shortcuts, x => x.Id == "selection");
    }

    [Fact]
    public void The_status_never_holds_the_key()
    {
        var h = Build(new AppSettings(), "sk-fake-SECRETKEY");
        Assert.DoesNotContain("SECRETKEY", System.Text.Json.JsonSerializer.Serialize(h));
    }

    [Fact]
    public void An_unknown_provider_falls_back_to_the_default_preset() =>
        Assert.Equal(ProviderPresets.Groq.DisplayName, Build(new AppSettings { Primary = new EndpointSettings { Provider = "nope" } }, null).Provider);
}
