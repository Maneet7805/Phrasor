using System.Text.Json;
using Phrasor.Core.Bridge;
using Phrasor.Core.Home;
using Phrasor.Core.Optimization;
using Phrasor.Core.Settings;
using Phrasor.Core.Theme;
using Phrasor.Core.Web;

namespace Phrasor.Core.Tests;

public class BridgeDispatcherTests
{
    private const string Origin = WebSecurityPolicy.StartUrl;

    internal sealed class FakeApi : IMainWindowApi
    {
        public ThemeMode Theme = ThemeMode.Dark;
        public List<WindowAction> Windows = [];
        public List<Uri> Links = [];
        public Exception? Throw;
        public MainWindowState GetState() => Throw is null ? new(Theme, Dark: Theme == ThemeMode.Dark, Maximised: false) : throw Throw;
        public void SetTheme(ThemeMode mode) => Theme = mode;
        public void Window(WindowAction action) => Windows.Add(action);
        public void OpenLink(Uri url) => Links.Add(url);

        public HomeStatus Home = new(HomeState.Ready, false, "Groq (free tier)", "openai/gpt-oss-120b", true, true, OptimizeMode.Standard,
            [new ShortcutStatus("optimize", "Rewrite", "Ctrl+Alt+P", true)]);
        public int? NextRun = 7; // null means a run is already going
        public List<TryItRequest> Runs = [];
        public int Cancels;
        public bool CanCopy = true;
        public bool Paused;
        public HomeStatus GetHome() => Home;
        public bool SetPaused(bool paused) => Paused = paused;
        public int? StartTryIt(TryItRequest request) { if (NextRun is not null) Runs.Add(request); return NextRun; }
        public void CancelTryIt() => Cancels++;
        public bool CopyRewrite() => CanCopy;

        public AppSettings Settings = new();
        public SettingsDraft? Saved, Tested;
        public int? NextTest = 3;
        public SettingsView GetSettings() => SettingsPage.BuildView(Settings, new SettingsTests.MemorySecrets(), false);
        public SettingsView SaveSettings(SettingsDraft draft) { Saved = draft; return GetSettings(); }
        public int? StartConnectionTest(SettingsDraft draft) { if (NextTest is not null) Tested = draft; return NextTest; }
    }

    private readonly FakeApi _api = new();
    private readonly List<string> _log = [];
    private long _now = 1_000;

    private BridgeDispatcher Make() => new(_api, _log.Add, () => _now);

    private static string Msg(string type, string? payload = null, string id = "a1") =>
        $$"""{"id":"{{id}}","type":"{{type}}"{{(payload is null ? "" : $",\"payload\":{payload}")}}}""";

    private static (bool Ok, string? Error, string? Id) Parse(string? reply)
    {
        using var doc = JsonDocument.Parse(reply!);
        var r = doc.RootElement;
        return (r.GetProperty("ok").GetBoolean(),
            r.TryGetProperty("error", out var e) ? e.GetString() : null,
            r.TryGetProperty("id", out var i) ? i.GetString() : null);
    }

    [Fact]
    public void State_get_returns_theme_and_window_flags()
    {
        var reply = Make().Handle(Origin, true, Msg("state.get"));
        using var doc = JsonDocument.Parse(reply!);
        var p = doc.RootElement.GetProperty("payload");
        Assert.Equal("dark", p.GetProperty("theme").GetString());
        Assert.True(p.GetProperty("dark").GetBoolean());
        Assert.False(p.GetProperty("maximised").GetBoolean());
    }

    [Theory]
    [InlineData("light", ThemeMode.Light)]
    [InlineData("dark", ThemeMode.Dark)]
    [InlineData("system", ThemeMode.System)]
    public void Theme_set_applies_the_mode(string name, ThemeMode expected)
    {
        var reply = Make().Handle(Origin, true, Msg("theme.set", $$"""{"mode":"{{name}}"}"""));
        Assert.True(Parse(reply).Ok);
        Assert.Equal(expected, _api.Theme);
    }

    [Theory]
    [InlineData("""{"mode":"purple"}""")]
    [InlineData("""{"mode":"Light"}""")]
    [InlineData("""{"mode":1}""")]
    [InlineData("""{"mode":"dark","extra":"x"}""")]
    [InlineData("""{}""")]
    [InlineData("""[]""")]
    [InlineData("\"dark\"")]
    public void Theme_set_with_a_bad_payload_changes_nothing(string payload)
    {
        var reply = Make().Handle(Origin, true, Msg("theme.set", payload));
        Assert.Equal((false, BridgeError.BadRequest, "a1"), Parse(reply));
        Assert.Equal(ThemeMode.Dark, _api.Theme);
    }

    [Fact]
    public void Theme_set_without_a_payload_is_refused() =>
        Assert.Equal(BridgeError.BadRequest, Parse(Make().Handle(Origin, true, Msg("theme.set"))).Error);

    [Theory]
    [InlineData("window.minimize", WindowAction.Minimize)]
    [InlineData("window.maximize", WindowAction.ToggleMaximize)]
    [InlineData("window.close", WindowAction.Close)]
    public void Window_commands_run_their_action(string type, WindowAction expected)
    {
        Assert.True(Parse(Make().Handle(Origin, true, Msg(type))).Ok);
        Assert.Equal([expected], _api.Windows);
    }

    [Fact]
    public void Window_commands_take_no_arguments()
    {
        Assert.Equal(BridgeError.BadRequest, Parse(Make().Handle(Origin, true, Msg("window.close", """{"force":true}"""))).Error);
        Assert.Empty(_api.Windows);
    }

    [Theory]
    [InlineData("groq-keys", "https://console.groq.com/keys")]
    [InlineData("project", "https://github.com/Maneet7805/Phrasor")]
    public void Link_open_opens_only_listed_links(string id, string url)
    {
        Assert.True(Parse(Make().Handle(Origin, true, Msg("link.open", $$"""{"id":"{{id}}"}"""))).Ok);
        Assert.Equal([new Uri(url)], _api.Links);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://console.groq.com/keys")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("javascript:alert(1)")]
    [InlineData("nope")]
    public void Link_open_refuses_urls_and_unknown_ids(string id)
    {
        Assert.Equal(BridgeError.BadRequest, Parse(Make().Handle(Origin, true, Msg("link.open", $$"""{"id":"{{id}}"}"""))).Error);
        Assert.Empty(_api.Links);
    }

    [Fact]
    public void Link_open_does_not_accept_a_url_field() =>
        Assert.Equal(BridgeError.BadRequest, Parse(Make().Handle(Origin, true, Msg("link.open", """{"url":"https://console.groq.com/keys"}"""))).Error);

    [Fact]
    public void Every_listed_link_is_https()
    {
        foreach (var id in LinkAllowList.Ids)
        {
            Assert.True(LinkAllowList.TryGet(id, out var url));
            Assert.True(LinkAllowList.IsAllowed(url));
            Assert.Equal(Uri.UriSchemeHttps, url.Scheme);
        }
        Assert.False(LinkAllowList.IsAllowed(new Uri("https://example.com/")));
    }

    [Fact]
    public void Unknown_command_gets_an_error_reply_with_its_id()
    {
        var reply = Make().Handle(Origin, true, Msg("history.list"));
        Assert.Equal((false, BridgeError.UnknownCommand, "a1"), Parse(reply));
    }

    [Fact]
    public void Command_list_has_nothing_that_types_pastes_or_submits()
    {
        var banned = new[] { "replace", "paste", "send", "key", "type", "submit", "enter", "secret", "eval", "navigate" };
        Assert.All(BridgeDispatcher.CommandNames, name => Assert.DoesNotContain(banned, b => name.Contains(b, StringComparison.OrdinalIgnoreCase)));
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("[[[[[[[[[[[[[1]]]]]]]]]]]]]")]
    public void Bad_json_gets_an_error_reply(string message) =>
        Assert.Equal(BridgeError.BadJson, Parse(Make().Handle(Origin, true, message)).Error);

    [Fact]
    public void A_non_string_message_gets_an_error_reply() =>
        Assert.Equal(BridgeError.BadJson, Parse(Make().Handle(Origin, true, null)).Error);

    [Theory]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("""{"type":"state.get"}""")]
    [InlineData("""{"id":"a1"}""")]
    [InlineData("""{"id":5,"type":"state.get"}""")]
    [InlineData("""{"id":"a1","type":7}""")]
    [InlineData("""{"id":"","type":"state.get"}""")]
    [InlineData("""{"id":"a b","type":"state.get"}""")]
    [InlineData("""{"id":"a1","type":"state.get","extra":1}""")]
    public void Wrong_shapes_get_a_bad_request_without_an_id(string message)
    {
        var reply = Make().Handle(Origin, true, message);
        Assert.Equal((false, BridgeError.BadRequest, (string?)null), Parse(reply));
    }

    [Fact]
    public void A_too_long_id_is_refused() =>
        Assert.Equal(BridgeError.BadRequest, Parse(Make().Handle(Origin, true, Msg("state.get", id: new string('x', BridgeDispatcher.MaxIdLength + 1)))).Error);

    [Fact]
    public void Oversize_messages_are_refused_before_parsing()
    {
        var message = Msg("theme.set", $$"""{"mode":"{{new string('x', BridgeDispatcher.MaxMessageBytes)}}"}""");
        Assert.Equal(BridgeError.TooLarge, Parse(Make().Handle(Origin, true, message)).Error);
        Assert.Equal(ThemeMode.Dark, _api.Theme);
    }

    [Fact]
    public void Size_is_counted_in_bytes_not_characters()
    {
        var message = new string('é', BridgeDispatcher.MaxMessageBytes / 2 + 1);
        Assert.Equal(BridgeError.TooLarge, Parse(Make().Handle(Origin, true, message)).Error);
    }

    [Fact]
    public void Too_many_messages_in_a_window_are_refused_until_the_window_passes()
    {
        var d = Make();
        for (var i = 0; i < BridgeDispatcher.MaxMessagesPerWindow; i++) Assert.True(Parse(d.Handle(Origin, true, Msg("state.get"))).Ok);
        Assert.Equal(BridgeError.RateLimited, Parse(d.Handle(Origin, true, Msg("state.get"))).Error);
        _now += BridgeDispatcher.WindowMs;
        Assert.True(Parse(d.Handle(Origin, true, Msg("state.get"))).Ok);
    }

    [Fact]
    public void A_flood_writes_one_rate_limit_log_line_per_window()
    {
        var d = Make();
        for (var i = 0; i < BridgeDispatcher.MaxMessagesPerWindow + 50; i++) d.Handle(Origin, true, Msg("state.get"));
        Assert.Single(_log, l => l.Contains("rate_limited"));
    }

    [Theory]
    [InlineData("https://evil.example/", true)]
    [InlineData("https://ui.phrasor.test.evil.com/index.html", true)]
    [InlineData("http://ui.phrasor.test/index.html", true)]
    [InlineData("file:///C:/x.html", true)]
    [InlineData("about:blank", true)]
    [InlineData(null, true)]
    [InlineData(Origin, false)] // our origin, but not the top frame
    public void Messages_from_other_origins_or_frames_are_dropped_and_counted(string? source, bool topFrame)
    {
        var d = Make();
        Assert.Null(d.Handle(source, topFrame, Msg("window.close")));
        Assert.Equal(1, d.DroppedCount);
        Assert.Empty(_api.Windows);
    }

    [Fact]
    public void A_throwing_api_becomes_a_failed_reply_and_the_log_names_only_the_exception_type()
    {
        _api.Throw = new InvalidOperationException("SECRETWORD in the message");
        var reply = Make().Handle(Origin, true, Msg("state.get"));
        Assert.Equal((false, BridgeError.Failed, (string?)null), Parse(reply));
        Assert.Contains("InvalidOperationException", _log.Single());
        Assert.DoesNotContain("SECRETWORD", _log.Single());
        Assert.DoesNotContain("SECRETWORD", reply);
    }

    [Fact]
    public void Logs_and_replies_never_contain_payload_text()
    {
        var d = Make();
        var replies = new[]
        {
            d.Handle(Origin, true, Msg("theme.set", """{"mode":"MARKERWORD"}""")),
            d.Handle(Origin, true, Msg("MARKERWORD")),
            d.Handle(Origin, true, Msg("link.open", """{"id":"MARKERWORD"}""")),
            d.Handle(Origin, true, """{"id":"a1","type":"state.get","MARKERWORD":1}"""),
            d.Handle(Origin, true, "MARKERWORD {"),
            d.Handle("https://MARKERWORD.example/", true, Msg("state.get")),
        };
        Assert.DoesNotContain(replies, r => r is not null && r.Contains("MARKERWORD"));
        Assert.NotEmpty(_log);
        Assert.DoesNotContain(_log, l => l.Contains("MARKERWORD"));
    }

    [Fact]
    public void Describe_writes_a_fixed_line_and_ignores_made_up_names()
    {
        Assert.Equal("bridge cmd=state.get bytes=40 ms=2 outcome=ok", BridgeLog.Describe("state.get", 40, 2, BridgeLog.Ok));
        Assert.Equal("bridge cmd=- bytes=1 ms=0 outcome=?", BridgeLog.Describe("MARKERWORD", 1, 0, "MARKERWORD"));
    }

    [Fact]
    public void Every_message_is_logged_once_with_its_outcome()
    {
        var d = Make();
        d.Handle(Origin, true, Msg("state.get"));
        d.Handle(Origin, true, Msg("nope"));
        Assert.Equal(["bridge cmd=state.get bytes=30 ms=0 outcome=ok", "bridge cmd=- bytes=25 ms=0 outcome=unknown_command"], _log);
    }
}
