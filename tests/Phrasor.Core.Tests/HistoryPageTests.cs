using System.Text.Json;
using Phrasor.Core.Bridge;
using Phrasor.Core.History;
using Phrasor.Core.Settings;
using Phrasor.Core.Web;

namespace Phrasor.Core.Tests;

public class HistoryPageTests
{
    private const string Origin = WebSecurityPolicy.StartUrl;
    private const string Id = BridgeDispatcherTests.FakeApi.SomeId;

    private sealed class MemoryPersistence : IHistoryPersistence
    {
        public byte[]? Data;
        public int Writes;
        public byte[]? Read() => Data;
        public void Write(byte[] plain) { Data = plain; Writes++; }
        public void MoveAside() => Data = null;
        public void Delete() => Data = null;
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly DateTime Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static HistoryEntry Entry(string id, DateTime utc, string original = "o", string rewrite = "r") => new(id, utc, original, rewrite);

    // ---- search and groups ----

    [Fact]
    public void Entries_are_grouped_by_local_day_with_Today_and_Yesterday_labels()
    {
        var entries = new[] { Entry("a", Now.AddHours(-1)), Entry("b", Now.AddDays(-1)), Entry("c", Now.AddDays(-3)), Entry("d", Now.AddDays(-400)) };
        var v = HistoryView.Build(entries, HistoryLoadOutcome.Ok, 0, "", 0, Now, Utc);
        Assert.Equal(["Today", "Yesterday", "Friday 13 November", "12 October 2025"], v.Groups.Select(g => g.Label));
        Assert.Equal("11:00", v.Groups[0].Items[0].Time);
    }

    [Fact]
    public void The_local_day_decides_the_group_not_the_utc_day()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("t", TimeSpan.FromHours(10), "t", "t");
        var late = new DateTime(2026, 11, 15, 20, 0, 0, DateTimeKind.Utc); // 06:00 on the 16th there
        var v = HistoryView.Build([Entry("a", late)], HistoryLoadOutcome.Ok, 0, "", 0, Now, zone);
        Assert.Equal("Today", v.Groups.Single().Label);
        Assert.Equal("06:00", v.Groups.Single().Items.Single().Time);
    }

    [Fact]
    public void Search_needs_every_word_in_the_draft_or_the_rewrite_in_any_case()
    {
        var entries = new[] { Entry("a", Now, "fix the Login bug", "Please debug"), Entry("b", Now, "write a poem", "Compose a poem about login"), Entry("c", Now, "other", "other") };
        Assert.Equal(["a", "b"], Ids(HistoryView.Build(entries, HistoryLoadOutcome.Ok, 0, "LOGIN", 0, Now, Utc)));
        Assert.Equal(["b"], Ids(HistoryView.Build(entries, HistoryLoadOutcome.Ok, 0, "  poem   login ", 0, Now, Utc)));
        Assert.Empty(HistoryView.Build(entries, HistoryLoadOutcome.Ok, 0, "login zebra", 0, Now, Utc).Groups);
        var v = HistoryView.Build(entries, HistoryLoadOutcome.Ok, 0, "login", 0, Now, Utc);
        Assert.Equal((3, 2), (v.Total, v.Matches));
    }

    [Fact]
    public void Limit_keeps_the_newest_matches()
    {
        var entries = Enumerable.Range(0, 5).Select(i => Entry("e" + i, Now.AddMinutes(-i))).ToList();
        Assert.Equal(["e0", "e1", "e2"], Ids(HistoryView.Build(entries, HistoryLoadOutcome.Ok, 0, "", 3, Now, Utc)));
    }

    [Fact]
    public void Preview_is_one_short_line_and_the_list_never_carries_the_draft()
    {
        var text = "line one\n\n   line   two " + new string('x', 400);
        var item = HistoryView.Build([Entry("a", Now, "SECRET DRAFT", text)], HistoryLoadOutcome.Ok, 0, "", 0, Now, Utc).Groups[0].Items[0];
        Assert.StartsWith("line one line two xxx", item.Preview);
        Assert.Equal(HistoryView.PreviewChars + 1, item.Preview.Length);
        Assert.EndsWith("…", item.Preview);
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(item));
    }

    [Theory]
    [InlineData(HistoryLoadOutcome.Ok, null)]
    [InlineData(HistoryLoadOutcome.Empty, null)]
    [InlineData(HistoryLoadOutcome.MovedAside, "moved_aside")]
    [InlineData(HistoryLoadOutcome.Unavailable, "unavailable")]
    public void The_notice_reports_a_problem_with_the_file(HistoryLoadOutcome outcome, string? notice) =>
        Assert.Equal(notice, HistoryView.Build([], outcome, 0, "", 0, Now, Utc).Notice);

    private static string[] Ids(HistoryListView v) => v.Groups.SelectMany(g => g.Items).Select(i => i.Id).ToArray();

    // ---- retention ----

    [Fact]
    public void Prune_removes_only_older_entries_and_writes_only_when_something_goes()
    {
        var disk = new MemoryPersistence();
        var clock = new Clock(new DateTimeOffset(Now.AddDays(-40)));
        var store = new HistoryStore(disk, clock);
        var old = store.Add("old draft", "old rewrite");
        clock.Now = new DateTimeOffset(Now);
        var fresh = store.Add("new draft", "new rewrite");
        var writes = disk.Writes;

        Assert.Equal(0, store.Prune(Now.AddDays(-90)));
        Assert.Equal(writes, disk.Writes);
        Assert.Equal(1, store.Prune(Now.AddDays(-30)));
        Assert.Equal([fresh.Id], store.List().Select(e => e.Id));
        Assert.Null(store.Get(old.Id));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(30, 30)]
    [InlineData(90, 90)]
    [InlineData(7, 0)]
    [InlineData(-1, 0)]
    [InlineData(100000, 0)]
    public void Only_zero_thirty_and_ninety_days_are_kept(int input, int expected) => Assert.Equal(expected, HistoryRetention.Normalise(input));

    [Fact]
    public void Retention_is_off_by_default_and_a_bad_stored_value_loads_as_off()
    {
        Assert.Equal(0, new AppSettings().History.RetentionDays);
        var path = Path.Combine(Path.GetTempPath(), "phrasor-ret-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """{"History":{"RetentionDays":7}}""");
            Assert.Equal(0, SettingsStore.Load(path).History.RetentionDays);
            File.WriteAllText(path, """{"History":{"RetentionDays":90}}""");
            Assert.Equal(90, SettingsStore.Load(path).History.RetentionDays);
            File.WriteAllText(path, """{"History":null}""");
            Assert.Equal(0, SettingsStore.Load(path).History.RetentionDays);
        }
        finally { File.Delete(path); }
    }

    // ---- bridge ----

    private readonly BridgeDispatcherTests.FakeApi _api = new();
    private readonly List<string> _log = [];

    private string? Send(string type, string? payload = null)
    {
        var d = new BridgeDispatcher(_api, _log.Add, () => 0);
        return d.Handle(Origin, true, $$"""{"id":"h1","type":"{{type}}"{{(payload is null ? "" : $",\"payload\":{payload}")}}}""");
    }

    private static string? ErrorOf(string? reply) => JsonDocument.Parse(reply!).RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;

    [Fact]
    public void List_passes_a_trimmed_query_and_the_limit()
    {
        Assert.Null(ErrorOf(Send("history.list", """{"query":"  login ","limit":3}""")));
        Assert.Equal(["list:login:3"], _api.HistoryCalls);
    }

    [Theory]
    [InlineData("""{"query":"x"}""")]
    [InlineData("""{"query":"x","limit":3,"extra":1}""")]
    [InlineData("""{"query":5,"limit":3}""")]
    [InlineData("""{"query":"x","limit":"3"}""")]
    [InlineData("""{"query":"x","limit":501}""")]
    [InlineData("""{"query":"x","limit":-1}""")]
    [InlineData("""{"query":"x","limit":1.5}""")]
    [InlineData("[]")]
    public void List_refuses_a_payload_that_is_not_exactly_query_and_limit(string payload)
    {
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Send("history.list", payload)));
        Assert.Empty(_api.HistoryCalls);
    }

    [Fact]
    public void List_refuses_a_query_over_200_characters_and_accepts_exactly_200()
    {
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Send("history.list", $$"""{"query":"{{new string('a', 201)}}","limit":0}""")));
        Assert.Null(ErrorOf(Send("history.list", $$"""{"query":"{{new string('a', 200)}}","limit":0}""")));
    }

    [Fact]
    public void Get_returns_the_full_text_and_an_unknown_id_is_a_bad_request()
    {
        var reply = Send("history.get", $$"""{"id":"{{Id}}"}""")!;
        Assert.Contains("my rewrite", reply);
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Send("history.get", """{"id":"nope"}""")));
    }

    [Fact]
    public void Save_takes_no_payload_so_the_page_cannot_supply_text()
    {
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Send("history.save", """{"text":"hello"}""")));
        Assert.Empty(_api.HistoryCalls);
        Assert.Null(ErrorOf(Send("history.save")));
        Assert.Equal(["save"], _api.HistoryCalls);
    }

    [Fact]
    public void Save_reports_when_a_side_was_cut_and_not_otherwise()
    {
        _api.SaveResult = HistorySaveResult.SavedCut;
        Assert.Contains("\"cut\":true", Send("history.save"));
        _api.SaveResult = HistorySaveResult.Saved;
        Assert.Contains("\"cut\":false", Send("history.save"));
    }

    [Fact]
    public void WouldCut_is_true_only_past_the_limit_on_either_side()
    {
        var max = new string('a', HistoryStore.MaxFieldChars);
        Assert.False(HistoryStore.WouldCut(max, max));
        Assert.True(HistoryStore.WouldCut(max + "a", "x"));
        Assert.True(HistoryStore.WouldCut("x", max + "a"));
    }

    [Fact]
    public void Save_reports_already_saved_and_nothing_to_save()
    {
        _api.SaveResult = HistorySaveResult.AlreadySaved;
        Assert.Contains("\"already\":true", Send("history.save"));
        _api.SaveResult = HistorySaveResult.NothingToSave;
        Assert.Equal(BridgeError.NothingToSave, ErrorOf(Send("history.save")));
    }

    [Fact]
    public void Copy_needs_an_id_and_a_known_part()
    {
        Assert.Contains("\"copied\":true", Send("history.copy", $$"""{"id":"{{Id}}","part":"original"}"""));
        Assert.Equal([$"copy:{Id}:original"], _api.HistoryCalls);
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Send("history.copy", $$"""{"id":"{{Id}}","part":"text"}""")));
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Send("history.copy", """{"id":"short","part":"rewrite"}""")));
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Send("history.copy", $$"""{"id":"{{Id}}","part":"rewrite","text":"x"}""")));
        Assert.Single(_api.HistoryCalls);
    }

    [Fact]
    public void Delete_one_and_delete_all()
    {
        Assert.Contains("\"deleted\":true", Send("history.delete", $$"""{"id":"{{Id}}"}"""));
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Send("history.delete", """{"confirm":true}""")));
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Send("history.deleteAll", """{"confirm":false}""")));
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Send("history.deleteAll")));
        Assert.DoesNotContain("deleteAll", _api.HistoryCalls);
        Assert.Null(ErrorOf(Send("history.deleteAll", """{"confirm":true}""")));
        Assert.Contains("deleteAll", _api.HistoryCalls);
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("30", true)]
    [InlineData("90", true)]
    [InlineData("7", false)]
    [InlineData("-30", false)]
    [InlineData("30.5", false)]
    [InlineData("\"30\"", false)]
    public void Retention_accepts_only_zero_thirty_and_ninety(string days, bool ok)
    {
        var error = ErrorOf(Send("history.setRetention", $$"""{"days":{{days}}}"""));
        Assert.Equal(ok ? null : BridgeError.BadRequest, error);
        Assert.Equal(ok ? [$"retention:{days}"] : [], _api.HistoryCalls);
    }

    [Fact]
    public void A_history_file_that_cannot_be_used_is_one_plain_error_kind()
    {
        _api.HistoryBroken = true;
        Assert.Equal(BridgeError.HistoryUnavailable, ErrorOf(Send("history.list", """{"query":"","limit":0}""")));
        Assert.Equal(BridgeError.HistoryUnavailable, ErrorOf(Send("history.save")));
    }

    [Fact]
    public void The_log_names_history_commands_but_never_their_text()
    {
        Send("history.list", """{"query":"ZEBRA-secret-word","limit":0}""");
        Send("history.copy", $$"""{"id":"{{Id}}","part":"rewrite"}""");
        Assert.Contains(_log, l => l.Contains("cmd=history.list") && l.Contains("outcome=ok"));
        Assert.DoesNotContain(_log, l => l.Contains("ZEBRA") || l.Contains(Id));
    }
}
