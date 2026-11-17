using System.Text.Json;
using System.Text.Json.Nodes;
using Phrasor.Core.Bridge;
using Phrasor.Core.Home;
using Phrasor.Core.Optimization;
using Phrasor.Core.Settings;
using Phrasor.Core.Web;

namespace Phrasor.Core.Tests;

/// <summary>The Shortcuts, Voice and Requirements parts of the Settings bridge (M8b).</summary>
public class SettingsShortcutsVoiceTests
{
    private readonly BridgeDispatcherTests.FakeApi _api = new();

    private static JsonObject Draft() => JsonNode.Parse("""
        {"general":{"theme":"light","defaultMode":"Detailed","reviewBeforeReplace":false,"startWithWindows":true,"sendAppHint":false,
          "repairDroppedDetails":false,"checkForUpdates":false,"personalContext":"x"},
         "model":{"primary":{"provider":"custom","model":"m","baseUrl":"https://example.test/v1"},
                  "fallback":{"enabled":false,"provider":"groq","model":"","baseUrl":""}},
         "keys":{"set":{},"remove":[]},
         "shortcuts":{"optimize":"ctrl+alt+O","optimizeSelection":"Ctrl+Alt+Shift+O","voice":""},
         "voice":{"enabled":true,"provider":"custom","model":" whisper-x ","language":"ms","autoOptimize":true,"maxSeconds":60},
         "requirements":{"All":"  Be brief.\r\n","Coding":"Find the root cause.","Debugging":"","Research":"","Study":"","Writing":"","Planning":"","General":"","Image":"","Video":""}}
        """)!.AsObject();

    private string? Send(string type, JsonNode? payload) =>
        new BridgeDispatcher(_api, _ => { }, () => 1_000).Handle(WebSecurityPolicy.StartUrl, true,
            new JsonObject { ["id"] = "s1", ["type"] = type, ["payload"] = payload?.DeepClone() }.ToJsonString());

    private static string? ErrorOf(string? reply) => JsonDocument.Parse(reply!).RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;

    [Fact]
    public void Save_parses_shortcuts_voice_and_requirements()
    {
        Assert.Null(ErrorOf(Send("settings.save", Draft())));
        var d = _api.Saved!;
        Assert.Equal(new ShortcutsDraft("ctrl+alt+O", "Ctrl+Alt+Shift+O", ""), d.Shortcuts);
        Assert.Equal(new VoiceDraft(true, "custom", "whisper-x", "ms", true, 60), d.Voice);
        Assert.Equal("Be brief.", d.Requirements["All"]);
        Assert.Equal(10, d.Requirements.Count);
    }

    [Fact]
    public void Requirements_over_the_limit_are_too_large_and_the_limit_is_allowed()
    {
        var d = Draft();
        d["requirements"]!["Coding"] = new string('r', RequirementsAppender.MaxLength + 1);
        Assert.Equal(BridgeError.TooLarge, ErrorOf(Send("settings.save", d)));
        d["requirements"]!["Coding"] = new string('r', RequirementsAppender.MaxLength);
        Assert.Null(ErrorOf(Send("settings.save", d)));
    }

    [Fact]
    public void A_full_set_of_worst_case_texts_fits_the_settings_limit()
    {
        var d = Draft();
        foreach (var id in SettingsPage.RequirementIds) d["requirements"]![id] = new string('"', RequirementsAppender.MaxLength);
        d["general"]!["personalContext"] = new string('\\', SettingsPayload.MaxPersonalContextChars);
        var json = new JsonObject { ["id"] = "s1", ["type"] = "settings.save", ["payload"] = d }.ToJsonString();
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(json) < BridgeDispatcher.MaxSettingsBytes);
        Assert.Null(ErrorOf(Send("settings.save", d)));
    }

    [Fact]
    public void Apply_writes_canonical_chords_voice_and_non_empty_requirements()
    {
        Send("settings.save", Draft());
        var s = new AppSettings();
        s.StandingRequirements["Writing"] = "old";
        SettingsPage.Apply(s, _api.Saved!);
        Assert.Equal("Ctrl+Alt+O", s.Hotkeys.Optimize);
        Assert.Equal("", s.Hotkeys.Voice);
        Assert.Equal("custom", s.Voice.Provider);
        Assert.Equal("https://example.test/v1", s.Voice.BaseUrl); // a custom voice endpoint uses the main model's address, as in the classic window
        Assert.Equal(60, s.Voice.MaxSeconds);
        Assert.Equal(["All", "Coding"], s.StandingRequirements.Keys.Order());
    }

    private static string? Probe(string chord) => chord == "Ctrl+Alt+K" ? "Ctrl+Alt+K is already used by another app." : null;

    [Theory]
    [InlineData("Ctrl+Alt+P", "Ctrl+Alt+Shift+P", "Ctrl+Alt+D", 0)]
    [InlineData("", "", "", 0)]
    [InlineData("P", "Ctrl+Alt+Shift+P", "Ctrl+Alt+D", 1)]           // no Ctrl or Alt
    [InlineData("Ctrl+Alt", "Ctrl+Alt+Shift+P", "Ctrl+Alt+D", 1)]    // no key
    [InlineData("Ctrl+Alt+P", "ctrl+alt+p", "Ctrl+Alt+D", 1)]        // the same chord twice
    [InlineData("Ctrl+Alt+K", "Ctrl+Alt+Shift+P", "Ctrl+Alt+D", 1)]  // held by another app
    [InlineData("Ctrl+Alt+Delete", "Ctrl+Alt+Shift+P", "Ctrl+Alt+D", 1)]
    public void Shortcut_problems_are_found_with_a_plain_message(string a, string b, string c, int expected)
    {
        var notes = SettingsPage.CheckShortcuts(new ShortcutsDraft(a, b, c), voiceOn: true, Probe);
        Assert.Equal(expected, notes.Count);
    }

    [Fact]
    public void The_voice_chord_is_not_checked_while_voice_is_off()
    {
        var d = new ShortcutsDraft("Ctrl+Alt+P", "Ctrl+Alt+Shift+P", "Ctrl+Alt+K");
        Assert.Empty(SettingsPage.CheckShortcuts(d, voiceOn: false, Probe));
        Assert.Equal(HomeStatus.VoiceId, Assert.Single(SettingsPage.CheckShortcuts(d, voiceOn: true, Probe)).Id);
    }

    [Fact]
    public void Record_start_and_stop_reach_the_app_and_take_no_payload()
    {
        Assert.Null(ErrorOf(Send("shortcut.recordStart", null)));
        Assert.Null(ErrorOf(Send("shortcut.recordStop", null)));
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Send("shortcut.recordStart", new JsonObject { ["x"] = 1 })));
        Assert.Equal([true, false], _api.Recording);
    }

    [Fact]
    public void Save_reply_says_whether_it_saved_and_carries_the_settings()
    {
        var payload = JsonDocument.Parse(Send("settings.save", Draft())!).RootElement.GetProperty("payload");
        Assert.True(payload.GetProperty("saved").GetBoolean());
        Assert.Equal(JsonValueKind.Object, payload.GetProperty("settings").ValueKind);
        Assert.Equal(0, payload.GetProperty("problems").GetArrayLength());
    }

    [Fact]
    public void The_view_lists_every_task_box()
    {
        var s = new AppSettings();
        s.StandingRequirements["coding"] = "Root cause.";
        var v = SettingsPage.BuildView(s, new SettingsTests.MemorySecrets(), false);
        Assert.Equal(["All", "Coding", "Debugging", "Research", "Study", "Writing", "Planning", "General", "Image", "Video"], v.Requirements.Select(r => r.Id));
        Assert.Equal("Root cause.", v.Requirements.Single(r => r.Id == "Coding").Text);
        Assert.Equal(RequirementsAppender.MaxLength, v.RequirementsMax);
    }
}
