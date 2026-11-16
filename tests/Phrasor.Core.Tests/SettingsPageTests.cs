using System.Text.Json;
using System.Text.Json.Nodes;
using Phrasor.Core.Bridge;
using Phrasor.Core.Llm;
using Phrasor.Core.Optimization;
using Phrasor.Core.Settings;
using Phrasor.Core.Theme;
using Phrasor.Core.Web;

namespace Phrasor.Core.Tests;

public class SettingsPageTests
{
    private const string Origin = WebSecurityPolicy.StartUrl;
    private const string FakeKey = "FAKE-KEY-MARKER-123";
    private readonly BridgeDispatcherTests.FakeApi _api = new();
    private readonly List<string> _log = [];

    private BridgeDispatcher Make() => new(_api, _log.Add, () => 1_000);

    private static JsonObject Draft() => JsonNode.Parse("""
        {"general":{"theme":"light","defaultMode":"Detailed","reviewBeforeReplace":false,"startWithWindows":true,"sendAppHint":false,
          "repairDroppedDetails":false,"checkForUpdates":false,"personalContext":"  Year 3 CS student.  "},
         "model":{"primary":{"provider":"custom","model":" my-model ","baseUrl":"https://example.test/v1"},
                  "fallback":{"enabled":false,"provider":"groq","model":"","baseUrl":"https://ignored.test"}},
         "keys":{"set":{"custom":"  FAKE-KEY-MARKER-123  "},"remove":["gemini"]},
         "shortcuts":{"optimize":"ctrl+alt+O","optimizeSelection":"Ctrl+Alt+Shift+O","voice":""},
         "voice":{"enabled":true,"provider":"custom","model":" whisper-x ","language":"ms","autoOptimize":true,"maxSeconds":60},
         "requirements":{"All":"  Be brief.\r\n","Coding":"Find the root cause.","Debugging":"","Research":"","Study":"","Writing":"","Planning":"","General":"","Image":""}}
        """)!.AsObject();

    private string? Send(string type, JsonNode? payload, string id = "s1") =>
        Make().Handle(Origin, true, new JsonObject { ["id"] = id, ["type"] = type, ["payload"] = payload?.DeepClone() }.ToJsonString());

    private static string? ErrorOf(string? reply) => JsonDocument.Parse(reply!).RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;

    [Fact]
    public void Save_parses_the_draft_and_hands_it_to_the_app()
    {
        var reply = Send("settings.save", Draft());
        Assert.Null(ErrorOf(reply));
        var d = _api.Saved!;
        Assert.Equal(ThemeMode.Light, d.Theme);
        Assert.Equal(OptimizeMode.Detailed, d.DefaultMode);
        Assert.False(d.ReviewBeforeReplace);
        Assert.True(d.StartWithWindows);
        Assert.Equal("Year 3 CS student.", d.PersonalContext);
        Assert.Equal(new EndpointDraft("custom", "my-model", "https://example.test/v1", true), d.Primary);
        Assert.Equal(new EndpointDraft("groq", "", null, false), d.Fallback); // a preset provider ignores a typed address
        Assert.Equal(FakeKey, d.NewKeys["custom"]);
        Assert.Equal(["gemini"], d.RemoveKeys);
    }

    [Fact]
    public void Test_connection_takes_the_same_draft_and_returns_a_run_number()
    {
        var reply = Send("settings.testConnection", Draft());
        Assert.Equal(3, JsonDocument.Parse(reply!).RootElement.GetProperty("payload").GetProperty("run").GetInt32());
        Assert.Null(_api.Saved);
        Assert.NotNull(_api.Tested);
    }

    [Fact]
    public void A_test_while_one_is_going_is_busy()
    {
        _api.NextTest = null;
        Assert.Equal(BridgeError.Busy, ErrorOf(Send("settings.testConnection", Draft())));
        Assert.Null(_api.Tested);
    }

    public static IEnumerable<object[]> BadDrafts()
    {
        var cases = new List<object[]>();
        void Case(string name, Action<JsonObject> change) => cases.Add([name, change]);
        Case("unknown top field", d => d["extra"] = 1);
        Case("missing keys", d => d.Remove("keys"));
        Case("unknown general field", d => d["general"]!["extra"] = true);
        Case("theme not listed", d => d["general"]!["theme"] = "purple");
        Case("mode is a number", d => d["general"]!["defaultMode"] = 1);
        Case("flag is a string", d => d["general"]!["reviewBeforeReplace"] = "yes");
        Case("provider not listed", d => d["model"]!["primary"]!["provider"] = "evil");
        Case("primary model blank", d => d["model"]!["primary"]!["model"] = "  ");
        Case("model has control character", d => d["model"]!["primary"]!["model"] = "a\u0007b");
        Case("model too long", d => d["model"]!["primary"]!["model"] = new string('m', 101));
        Case("address is not http", d => d["model"]!["primary"]!["baseUrl"] = "file:///c:/x");
        Case("address has a password", d => d["model"]!["primary"]!["baseUrl"] = "https://u:p@example.test");
        Case("primary has an enabled flag", d => d["model"]!["primary"]!["enabled"] = true);
        Case("key for an unknown provider", d => d["keys"]!["set"]!["ollama"] = "k");
        Case("blank key", d => d["keys"]!["set"]!["groq"] = "   ");
        Case("key with control character", d => d["keys"]!["set"]!["groq"] = "a\nb");
        Case("key too long", d => d["keys"]!["set"]!["groq"] = new string('k', 501));
        Case("key both set and removed", d => d["keys"]!["remove"] = new JsonArray("custom"));
        Case("remove is not a list", d => d["keys"]!["remove"] = "groq");
        Case("remove names an unknown id", d => d["keys"]!["remove"] = new JsonArray("ollama"));
        Case("missing shortcuts", d => d.Remove("shortcuts"));
        Case("unknown shortcut field", d => d["shortcuts"]!["extra"] = "x");
        Case("shortcut is a number", d => d["shortcuts"]!["voice"] = 5);
        Case("shortcut too long", d => d["shortcuts"]!["voice"] = new string('a', 41));
        Case("shortcut has control character", d => d["shortcuts"]!["voice"] = "Ctrl+\u0007");
        Case("voice provider not listed", d => d["voice"]!["provider"] = "gemini");
        Case("voice on with no model", d => { d["voice"]!["enabled"] = true; d["voice"]!["model"] = " "; });
        Case("voice model too long", d => d["voice"]!["model"] = new string('m', 101));
        Case("language is not a code", d => d["voice"]!["language"] = "english please");
        Case("max seconds too small", d => d["voice"]!["maxSeconds"] = 9);
        Case("max seconds too big", d => d["voice"]!["maxSeconds"] = 601);
        Case("max seconds is a fraction", d => d["voice"]!["maxSeconds"] = 60.5);
        Case("max seconds is a string", d => d["voice"]!["maxSeconds"] = "60");
        Case("voice flag is a string", d => d["voice"]!["autoOptimize"] = "yes");
        Case("requirements missing a task", d => d["requirements"]!.AsObject().Remove("Image"));
        Case("requirements for Auto", d => d["requirements"]!["Auto"] = "x");
        Case("requirement is a number", d => d["requirements"]!["Coding"] = 1);
        Case("requirement has control character", d => d["requirements"]!["Coding"] = "a\u0000b");
        return cases;
    }

    [Theory]
    [MemberData(nameof(BadDrafts))]
    public void A_bad_draft_is_refused_and_nothing_is_started(string name, Action<JsonObject> change)
    {
        var d = Draft();
        change(d);
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Send("settings.save", d)));
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Send("settings.testConnection", d)));
        Assert.Null(_api.Saved);
        Assert.Null(_api.Tested);
        Assert.False(string.IsNullOrEmpty(name));
    }

    [Fact]
    public void A_non_object_payload_or_none_is_refused()
    {
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Send("settings.save", JsonValue.Create("x"))));
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Send("settings.save", null)));
    }

    [Fact]
    public void A_failed_save_is_a_plain_failed_reply_with_no_key_text()
    {
        _api.Throw = new IOException($"disk full while writing {FakeKey}");
        var reply = Send("settings.save", Draft());
        Assert.Equal(BridgeError.Failed, ErrorOf(reply));
        Assert.DoesNotContain(FakeKey, reply);
        Assert.All(_log, l => Assert.DoesNotContain(FakeKey, l));
    }

    [Fact]
    public void Apply_on_a_copy_leaves_the_live_settings_unchanged()
    {
        var live = new AppSettings();
        var next = SettingsStore.Clone(live);
        Send("settings.save", Draft());
        SettingsPage.Apply(next, _api.Saved!);
        Assert.NotEqual(next.Primary.Model, live.Primary.Model);
        Assert.Equal(new AppSettings().Primary.Model, live.Primary.Model);
    }

    [Fact]
    public void Too_long_about_me_text_is_too_large_and_the_limit_is_allowed_through()
    {
        var d = Draft();
        d["general"]!["personalContext"] = new string('x', SettingsPayload.MaxPersonalContextChars + 1);
        Assert.Equal(BridgeError.TooLarge, ErrorOf(Send("settings.save", d)));
        d["general"]!["personalContext"] = new string('x', SettingsPayload.MaxPersonalContextChars);
        Assert.Null(ErrorOf(Send("settings.save", d)));
    }

    [Fact]
    public void Settings_messages_may_be_bigger_than_the_default_but_other_commands_may_not()
    {
        var d = Draft();
        d["general"]!["personalContext"] = string.Concat(Enumerable.Repeat("é", 2_000)); // ~4 KB of text, over the default limit once escaped
        Assert.Null(ErrorOf(Send("settings.save", d)));
        Assert.Equal(BridgeError.TooLarge, ErrorOf(Send("theme.set", new JsonObject { ["mode"] = new string('x', 5_000) })));
        d["general"]!["personalContext"] = new string('x', BridgeDispatcher.MaxSettingsBytes);
        Assert.Equal(BridgeError.TooLarge, ErrorOf(Send("settings.save", d)));
    }

    [Fact]
    public void Get_returns_set_or_not_set_for_keys_and_never_a_key()
    {
        var secrets = new SettingsTests.MemorySecrets();
        secrets.Set("groq", FakeKey);
        var view = SettingsPage.BuildView(new AppSettings(), secrets, startWithWindows: true);
        var json = JsonSerializer.Serialize(view);
        Assert.DoesNotContain(FakeKey, json);
        Assert.True(view.Keys.Single(k => k.Id == "groq").Set);
        Assert.False(view.Keys.Single(k => k.Id == "gemini").Set);
        Assert.True(view.General.StartWithWindows);
        Assert.Equal(["groq", "gemini", "custom"], view.Keys.Select(k => k.Id));
    }

    [Fact]
    public void Key_links_are_ids_the_page_may_open()
    {
        var view = SettingsPage.BuildView(new AppSettings(), new SettingsTests.MemorySecrets(), false);
        Assert.All(view.Keys.Where(k => k.LinkId is not null), k => Assert.True(LinkAllowList.TryGet(k.LinkId!, out _)));
    }

    [Fact]
    public void Apply_writes_the_choices_and_leaves_other_settings_alone()
    {
        Send("settings.save", Draft());
        var s = new AppSettings { TimeoutSeconds = 99 };
        s.Judge.Model = "judge-model";
        SettingsPage.Apply(s, _api.Saved!);
        Assert.Equal(ThemeMode.Light, s.Ui.Theme);
        Assert.Equal("custom", s.Primary.Provider);
        Assert.Equal("https://example.test/v1", s.Primary.BaseUrl);
        Assert.False(s.Fallback.Enabled);
        Assert.True(s.FirstRunComplete);
        Assert.Equal(99, s.TimeoutSeconds);
        Assert.Equal("Ctrl+Alt+O", s.Hotkeys.Optimize); // shortcuts are now part of the draft
        Assert.Equal("judge-model", s.Judge.Model);
    }

    [Fact]
    public void Clone_is_independent_of_the_original()
    {
        var a = new AppSettings();
        var b = SettingsStore.Clone(a);
        b.Primary.Model = "changed";
        Assert.NotEqual("changed", a.Primary.Model);
    }

    // --- Test connection ---

    private sealed class FakeClient(Exception? fail = null) : ILlmClient
    {
        public LlmEndpoint? Seen;

        public Task<ChatResponse> CompleteAsync(LlmEndpoint endpoint, IReadOnlyList<ChatMessage> messages, ChatOptions options, CancellationToken ct)
        {
            Seen = endpoint;
            return fail is null ? Task.FromResult(new ChatResponse("OK", "stop", 1, 1, 0, endpoint.Model)) : Task.FromException<ChatResponse>(fail);
        }

        public Task<string> TranscribeAsync(LlmEndpoint endpoint, byte[] audio, string fileName, string? language, TimeSpan timeout, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task Test_uses_the_typed_key_over_the_saved_one_and_reports_the_label()
    {
        var saved = new SettingsTests.MemorySecrets();
        saved.Set("groq", "saved-key");
        var client = new FakeClient();
        var typed = new Dictionary<string, string> { ["groq"] = "typed-key" };
        var result = await SettingsPage.TestAsync(client, new AppSettings(), new OverlaySecretStore(saved, typed), default);
        Assert.True(result.Ok);
        Assert.Equal("typed-key", client.Seen!.ApiKey);
        Assert.Equal("openai/gpt-oss-120b (groq)", result.Label);
        Assert.Equal("saved-key", saved.Get("groq")); // nothing was saved
    }

    [Fact]
    public async Task Test_without_a_key_says_no_key_and_never_calls_the_provider()
    {
        var client = new FakeClient();
        var result = await SettingsPage.TestAsync(client, new AppSettings(), new SettingsTests.MemorySecrets(), default);
        Assert.Equal(TryItErrors.NoKey, result.Error);
        Assert.Null(client.Seen);
    }

    [Theory]
    [InlineData(LlmErrorKind.Auth, TryItErrors.KeyRejected)]
    [InlineData(LlmErrorKind.ModelNotFound, TryItErrors.ModelNotFound)]
    [InlineData(LlmErrorKind.Network, TryItErrors.Offline)]
    public async Task A_provider_failure_becomes_a_fixed_kind_without_the_provider_text(LlmErrorKind kind, string expected)
    {
        var secrets = new SettingsTests.MemorySecrets();
        secrets.Set("groq", "k");
        var failure = new LlmException(kind, "PROVIDER-SAID-" + FakeKey);
        var result = await SettingsPage.TestAsync(new FakeClient(failure), new AppSettings(), secrets, default);
        Assert.Equal(expected, result.Error);
        Assert.DoesNotContain("PROVIDER-SAID", JsonSerializer.Serialize(result));
    }

    [Fact]
    public void Test_events_match_the_fixtures()
    {
        var dir = Path.Combine(BridgeContractTests.ContractsDir(), "events");
        var done = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "settings.test.done.json")))!;
        var actual = BridgeEvents.SettingsTestDone(done["run"]!.GetValue<int>(),
            new ConnectionResult(true, done["result"]!["label"]!.GetValue<string>(), done["result"]!["ms"]!.GetValue<long>(), null));
        Assert.True(JsonNode.DeepEquals(done["event"], JsonNode.Parse(actual)), actual);

        var failed = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "settings.test.failed.json")))!;
        actual = BridgeEvents.SettingsTestFailed(failed["run"]!.GetValue<int>(), failed["error"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(failed["event"], JsonNode.Parse(actual)), actual);
    }

    [Fact]
    public void The_log_never_holds_a_key_or_the_about_me_text()
    {
        var d = Draft();
        d["general"]!["personalContext"] = "ABOUT-ME-MARKER";
        Send("settings.save", d);
        Send("settings.testConnection", d);
        var bad = Draft();
        bad["model"]!["primary"]!["provider"] = FakeKey;
        Send("settings.save", bad);
        Assert.NotEmpty(_log);
        Assert.All(_log, l =>
        {
            Assert.DoesNotContain(FakeKey, l);
            Assert.DoesNotContain("ABOUT-ME-MARKER", l);
        });
    }

    [Fact]
    public void An_error_reply_carries_no_payload_text()
    {
        var bad = Draft();
        bad["model"]!["primary"]!["provider"] = FakeKey;
        Assert.DoesNotContain(FakeKey, Send("settings.save", bad));
    }
}
