using System.Text;
using Phrasor.Core.History;

namespace Phrasor.Core.Tests;

public class HistoryStoreTests : IDisposable
{
    private const string Marker = "ZEBRA-MARKER-4471";

    private sealed class FakePersistence : IHistoryPersistence
    {
        public byte[]? Data;
        public bool MovedAside, Deleted, FailWrites, FailRead, FailMove;
        public int Writes;
        public byte[]? Read() => FailRead ? throw new IOException("locked") : Data;
        public void Write(byte[] plain) { if (FailWrites) throw new IOException("disk full"); Data = plain; Writes++; }
        public void MoveAside() { if (FailMove) throw new IOException("denied"); MovedAside = true; Data = null; }
        public void Delete() { Deleted = true; Data = null; }
    }

    /// <summary>Not real encryption: reversible and unreadable as text, and it throws on data it did not make.</summary>
    private sealed class XorProtector : IBlobProtector
    {
        public byte[] Protect(byte[] plain) => [.. plain.Select(b => (byte)(b ^ 0x5A)), 0x7E];
        public byte[] Unprotect(byte[] data) => data.Length > 0 && data[^1] == 0x7E ? [.. data[..^1].Select(b => (byte)(b ^ 0x5A))] : throw new InvalidDataException("not ours");
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "phrasor-history-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }
    private string FilePath => Path.Combine(_dir, "history.dat");
    private HistoryStore OpenFileStore() => new(new EncryptedHistoryFile(FilePath, new XorProtector()));

    [Fact]
    public void Add_then_list_and_get_return_the_entry_newest_first()
    {
        var store = new HistoryStore(new FakePersistence());
        var a = store.Add("draft a", "rewrite a");
        var b = store.Add("draft b", "rewrite b");
        Assert.Equal([b.Id, a.Id], store.List().Select(e => e.Id));
        Assert.Equal("rewrite a", store.Get(a.Id)!.Rewrite);
        Assert.Null(store.Get("nope"));
    }

    [Fact]
    public void Nothing_is_stored_until_Add_is_called()
    {
        var fake = new FakePersistence();
        var store = new HistoryStore(fake);
        Assert.Empty(store.List());
        Assert.Equal(0, fake.Writes);
        Assert.Equal(HistoryLoadOutcome.Empty, store.LoadOutcome);
    }

    [Fact]
    public void Ids_are_random_and_times_are_utc()
    {
        var store = new HistoryStore(new FakePersistence());
        var a = store.Add("same", "same");
        var b = store.Add("same", "same");
        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(32, a.Id.Length);
        Assert.DoesNotContain("same", a.Id);
        Assert.Equal(DateTimeKind.Utc, a.SavedUtc.Kind);
    }

    [Fact]
    public void Add_rejects_an_empty_rewrite_without_putting_text_in_the_message()
    {
        var store = new HistoryStore(new FakePersistence());
        var ex = Assert.Throws<ArgumentException>(() => store.Add(Marker, "  "));
        Assert.DoesNotContain(Marker, ex.Message);
        Assert.Empty(store.List());
    }

    [Fact]
    public void Delete_removes_one_entry_and_reports_a_missing_one()
    {
        var store = new HistoryStore(new FakePersistence());
        var a = store.Add("a", "a");
        var b = store.Add("b", "b");
        Assert.True(store.Delete(a.Id));
        Assert.False(store.Delete(a.Id));
        Assert.Equal([b.Id], store.List().Select(e => e.Id));
    }

    [Fact]
    public void Clear_empties_the_store_and_deletes_the_stored_data()
    {
        var fake = new FakePersistence();
        var store = new HistoryStore(fake);
        store.Add("a", "a");
        store.Clear();
        Assert.Empty(store.List());
        Assert.True(fake.Deleted);
        Assert.Null(fake.Data);
    }

    [Fact]
    public void More_than_500_entries_drops_the_oldest()
    {
        var store = new HistoryStore(new FakePersistence());
        var first = store.Add("first", "first");
        for (var i = 0; i < HistoryStore.MaxEntries; i++) store.Add("d" + i, "r" + i);
        Assert.Equal(HistoryStore.MaxEntries, store.List().Count);
        Assert.Null(store.Get(first.Id));
        Assert.Equal("r" + (HistoryStore.MaxEntries - 1), store.List()[0].Rewrite);
        Assert.Equal("r0", store.List()[^1].Rewrite);
    }

    [Fact]
    public void Fields_are_cut_to_exactly_20000_characters()
    {
        var store = new HistoryStore(new FakePersistence());
        var e = store.Add(new string('a', 20_000), new string('b', 20_001));
        Assert.Equal(20_000, e.Original.Length);
        Assert.Equal(20_000, e.Rewrite.Length);
    }

    [Fact]
    public void Cutting_never_splits_a_surrogate_pair()
    {
        var text = new string('a', 19_999) + "😀tail";
        var cut = HistoryStore.Limit(text);
        Assert.Equal(19_999, cut.Length);
        Assert.False(char.IsHighSurrogate(cut[^1]));
        var whole = new string('a', 19_998) + "😀tail";
        Assert.Equal(20_000, HistoryStore.Limit(whole).Length);
    }

    [Fact]
    public void A_failed_write_throws_and_changes_nothing()
    {
        var fake = new FakePersistence();
        var store = new HistoryStore(fake);
        var a = store.Add("a", "a");
        fake.FailWrites = true;
        Assert.Throws<IOException>(() => store.Add("b", "b"));
        Assert.Throws<IOException>(() => store.Delete(a.Id));
        Assert.Equal([a.Id], store.List().Select(e => e.Id));
    }

    [Fact]
    public void Entries_survive_a_restart()
    {
        var fake = new FakePersistence();
        var id = new HistoryStore(fake).Add("draft", "rewrite").Id;
        var again = new HistoryStore(fake);
        Assert.Equal(HistoryLoadOutcome.Ok, again.LoadOutcome);
        Assert.Equal("draft", again.Get(id)!.Original);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"Version\":0,\"Entries\":[]}")]
    [InlineData("{\"Entries\":[]}")]
    [InlineData("{\"Version\":1}")]
    public void An_unreadable_or_unknown_version_is_moved_aside_and_the_store_starts_empty(string content)
    {
        var fake = new FakePersistence { Data = Encoding.UTF8.GetBytes(content) };
        var store = new HistoryStore(fake);
        Assert.Equal(HistoryLoadOutcome.MovedAside, store.LoadOutcome);
        Assert.True(fake.MovedAside);
        Assert.False(fake.Deleted);
        Assert.Empty(store.List());
        store.Add("x", "y"); // still usable
        Assert.Single(store.List());
    }

    [Fact]
    public void A_file_that_cannot_be_read_because_it_is_locked_is_left_alone_and_saving_is_refused()
    {
        var fake = new FakePersistence { FailRead = true };
        var store = new HistoryStore(fake);
        Assert.Equal(HistoryLoadOutcome.Unavailable, store.LoadOutcome);
        Assert.False(fake.MovedAside);
        Assert.Throws<InvalidOperationException>(() => store.Add("a", "b"));
        Assert.Throws<InvalidOperationException>(store.Clear);
    }

    [Fact]
    public void If_moving_the_bad_file_aside_fails_nothing_is_overwritten()
    {
        var fake = new FakePersistence { Data = "junk"u8.ToArray(), FailMove = true };
        var store = new HistoryStore(fake);
        Assert.Equal(HistoryLoadOutcome.Unavailable, store.LoadOutcome);
        Assert.Throws<InvalidOperationException>(() => store.Add("a", "b"));
        Assert.Equal("junk", Encoding.UTF8.GetString(fake.Data!));
    }

    [Fact]
    public void Oversize_and_surplus_entries_in_a_file_are_cut_to_the_limits_on_load()
    {
        var entries = Enumerable.Range(0, 600).Select(i => new { Id = "id" + i, SavedUtc = DateTime.UtcNow, Original = new string('o', 30_000), Rewrite = "r" + i });
        var json = System.Text.Json.JsonSerializer.Serialize(new { Version = 1, Entries = entries });
        var store = new HistoryStore(new FakePersistence { Data = Encoding.UTF8.GetBytes(json) });
        Assert.Equal(HistoryStore.MaxEntries, store.List().Count);
        Assert.All(store.List(), e => Assert.Equal(20_000, e.Original.Length));
        Assert.Equal("id0", store.List()[0].Id);
    }

    [Fact]
    public void Entries_with_no_id_or_no_rewrite_or_a_repeated_id_are_skipped_on_load()
    {
        const string json = "{\"Version\":1,\"Entries\":[null,{\"Id\":\"\",\"Rewrite\":\"x\"},{\"Id\":\"a\",\"Rewrite\":\" \"},{\"Id\":\"b\",\"Rewrite\":\"ok\"},{\"Id\":\"b\",\"Rewrite\":\"dup\"}]}";
        var store = new HistoryStore(new FakePersistence { Data = Encoding.UTF8.GetBytes(json) });
        Assert.Equal(["ok"], store.List().Select(e => e.Rewrite));
    }

    [Fact]
    public async Task Concurrent_adds_lose_nothing()
    {
        var store = new HistoryStore(new FakePersistence());
        await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 30; i++) store.Add("d", $"t{t}-{i}");
        })));
        Assert.Equal(240, store.List().Count);
        Assert.Equal(240, store.List().Select(e => e.Id).Distinct().Count());
    }

    // ---- the encrypted file ----

    [Fact]
    public void File_round_trip_keeps_entries_and_the_file_holds_no_plain_text()
    {
        var id = OpenFileStore().Add(Marker, "rewrite " + Marker).Id;
        Assert.DoesNotContain(Marker, File.ReadAllText(FilePath, Encoding.Latin1));
        var again = OpenFileStore();
        Assert.Equal(HistoryLoadOutcome.Ok, again.LoadOutcome);
        Assert.Equal(Marker, again.Get(id)!.Original);
    }

    [Fact]
    public void File_save_leaves_no_temp_file_and_no_plain_text_anywhere_in_the_folder()
    {
        var store = OpenFileStore();
        store.Add(Marker, Marker);
        store.Add(Marker, Marker);
        Assert.Equal([FilePath], Directory.GetFiles(_dir));
        Assert.All(Directory.GetFiles(_dir), f => Assert.DoesNotContain(Marker, File.ReadAllText(f, Encoding.Latin1)));
    }

    [Fact]
    public void File_delete_one_rewrites_the_file_without_that_entry()
    {
        var store = OpenFileStore();
        var gone = store.Add("gone-draft", "gone-rewrite " + Marker);
        store.Add("kept", "kept");
        var before = File.ReadAllBytes(FilePath);
        store.Delete(gone.Id);
        Assert.NotEqual(before, File.ReadAllBytes(FilePath));
        var plain = new XorProtector().Unprotect(File.ReadAllBytes(FilePath));
        Assert.DoesNotContain(Marker, Encoding.UTF8.GetString(plain));
        Assert.Equal(["kept"], OpenFileStore().List().Select(e => e.Rewrite));
    }

    [Fact]
    public void File_clear_deletes_the_file()
    {
        var store = OpenFileStore();
        store.Add("a", "b");
        store.Clear();
        Assert.False(File.Exists(FilePath));
        Assert.Equal(HistoryLoadOutcome.Empty, OpenFileStore().LoadOutcome);
    }

    [Fact]
    public void File_clear_also_removes_a_set_aside_dot_bad_copy()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(FilePath, "garbage"u8.ToArray());
        var store = OpenFileStore();
        store.Add("a", "b");
        store.Clear();
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void A_corrupt_file_is_kept_as_dot_bad_and_never_deleted()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(FilePath, "garbage"u8.ToArray());
        var store = OpenFileStore();
        Assert.Equal(HistoryLoadOutcome.MovedAside, store.LoadOutcome);
        Assert.False(File.Exists(FilePath));
        Assert.Equal("garbage", File.ReadAllText(FilePath + ".bad"));
        store.Add("a", "b");
        Assert.True(File.Exists(FilePath));
    }

    private static byte[] Header(byte version) => [.. "PHST"u8.ToArray(), version];

    [Fact]
    public void A_file_with_a_newer_version_byte_is_left_untouched_and_saving_is_refused()
    {
        Directory.CreateDirectory(_dir);
        byte[] newer = [.. Header(2), 1, 2, 3];
        File.WriteAllBytes(FilePath, newer);
        var store = OpenFileStore();
        Assert.Equal(HistoryLoadOutcome.Unavailable, store.LoadOutcome);
        Assert.Throws<InvalidOperationException>(() => store.Add("a", "b"));
        Assert.Throws<InvalidOperationException>(store.Clear);
        Assert.Equal(newer, File.ReadAllBytes(FilePath));
        Assert.Equal([FilePath], Directory.GetFiles(_dir));
    }

    [Fact]
    public void A_newer_Version_inside_the_data_is_left_untouched_too()
    {
        var fake = new FakePersistence { Data = "{\"Version\":2,\"Entries\":[]}"u8.ToArray() };
        var store = new HistoryStore(fake);
        Assert.Equal(HistoryLoadOutcome.Unavailable, store.LoadOutcome);
        Assert.False(fake.MovedAside);
    }

    [Fact]
    public void Saved_files_start_with_the_marker_and_version_byte()
    {
        OpenFileStore().Add("a", "b");
        Assert.Equal(Header(1), File.ReadAllBytes(FilePath)[..5]);
    }

    [Fact]
    public void A_file_without_a_header_still_loads_and_gets_one_on_the_next_save()
    {
        Directory.CreateDirectory(_dir);
        var json = "{\"Version\":1,\"Entries\":[{\"Id\":\"old\",\"SavedUtc\":\"2026-01-01T00:00:00Z\",\"Original\":\"o\",\"Rewrite\":\"legacy\"}]}"u8.ToArray();
        File.WriteAllBytes(FilePath, new XorProtector().Protect(json));
        var store = OpenFileStore();
        Assert.Equal(HistoryLoadOutcome.Ok, store.LoadOutcome);
        Assert.Equal("legacy", store.Get("old")!.Rewrite);
        store.Add("n", "new");
        Assert.Equal(Header(1), File.ReadAllBytes(FilePath)[..5]);
        Assert.Equal(2, OpenFileStore().List().Count);
    }

    [Theory]
    [InlineData(0)]
    public void A_header_with_version_zero_is_corrupt_and_kept_as_dot_bad(byte version)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(FilePath, [.. Header(version), 9, 9]);
        Assert.Equal(HistoryLoadOutcome.MovedAside, OpenFileStore().LoadOutcome);
        Assert.True(File.Exists(FilePath + ".bad"));
    }

    [Fact]
    public void A_truncated_header_is_corrupt_and_kept_as_dot_bad()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(FilePath, "PHST"u8.ToArray());
        Assert.Equal(HistoryLoadOutcome.MovedAside, OpenFileStore().LoadOutcome);
        Assert.True(File.Exists(FilePath + ".bad"));
    }

    [Fact]
    public void A_headed_file_that_cannot_be_decrypted_is_kept_as_dot_bad()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(FilePath, [.. Header(1), 1, 2, 3]);
        Assert.Equal(HistoryLoadOutcome.MovedAside, OpenFileStore().LoadOutcome);
        Assert.True(File.Exists(FilePath + ".bad"));
    }

    [Fact]
    public void Core_history_code_has_no_logging_or_hard_coded_path()
    {
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Phrasor.Core/History"));
        if (!Directory.Exists(dir)) return; // source not next to the build output
        foreach (var f in Directory.GetFiles(dir, "*.cs"))
        {
            var text = File.ReadAllText(f);
            Assert.DoesNotContain("Log.", text);
            Assert.DoesNotContain("AppData", text);
            Assert.DoesNotContain("Console.", text);
        }
    }
}
