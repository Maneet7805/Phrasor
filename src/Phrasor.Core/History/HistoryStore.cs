using System.Security.Cryptography;
using System.Text.Json;

namespace Phrasor.Core.History;

public enum HistoryLoadOutcome
{
    /// <summary>Loaded the stored entries.</summary>
    Ok,
    /// <summary>Nothing was stored yet.</summary>
    Empty,
    /// <summary>The file was unreadable; it was kept as a .bad copy and the list starts empty.</summary>
    MovedAside,
    /// <summary>The file could not be read or moved aside, or it was written by a newer Phrasor. It is left untouched, the list is empty and saving is refused so the file is never overwritten.</summary>
    Unavailable,
}

/// <summary>
/// Entries the user explicitly saved. Newest first, at most <see cref="MaxEntries"/>, each field at most <see cref="MaxFieldChars"/> characters.
/// Thread safe. Takes no logger and never puts entry text in an exception message.
/// </summary>
public sealed class HistoryStore
{
    public const int CurrentVersion = 1;
    public const int MaxEntries = 500;
    public const int MaxFieldChars = 20_000;

    private sealed record FileModel(int Version, List<HistoryEntry?>? Entries);

    private readonly IHistoryPersistence _persistence;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private IReadOnlyList<HistoryEntry> _entries = [];

    public HistoryLoadOutcome LoadOutcome { get; private set; }

    public HistoryStore(IHistoryPersistence persistence, TimeProvider? clock = null)
    {
        _persistence = persistence;
        _clock = clock ?? TimeProvider.System;
        Load();
    }

    /// <summary>Saves one entry (the only way an entry is created) and returns it. Throws if the file cannot be written; nothing changes then.</summary>
    public HistoryEntry Add(string original, string rewrite)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rewrite, nameof(rewrite));
        var entry = new HistoryEntry(RandomNumberGenerator.GetHexString(32, lowercase: true), _clock.GetUtcNow().UtcDateTime, Limit(original), Limit(rewrite));
        lock (_gate)
        {
            EnsureWritable();
            Commit([entry, .. _entries.Take(MaxEntries - 1)]);
        }
        return entry;
    }

    public IReadOnlyList<HistoryEntry> List()
    {
        lock (_gate) return _entries;
    }

    public HistoryEntry? Get(string id)
    {
        lock (_gate) return _entries.FirstOrDefault(e => e.Id == id);
    }

    public bool Delete(string id)
    {
        lock (_gate)
        {
            EnsureWritable();
            if (_entries.All(e => e.Id != id)) return false;
            Commit(_entries.Where(e => e.Id != id).ToList());
            return true;
        }
    }

    /// <summary>Removes entries saved before <paramref name="cutoffUtc"/> and returns how many. Writes nothing when none is that old.</summary>
    public int Prune(DateTime cutoffUtc)
    {
        lock (_gate)
        {
            var keep = _entries.Where(e => e.SavedUtc >= cutoffUtc).ToList();
            var removed = _entries.Count - keep.Count;
            if (removed == 0) return 0;
            EnsureWritable();
            Commit(keep);
            return removed;
        }
    }

    /// <summary>Removes every entry and deletes the file.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            EnsureWritable();
            _persistence.Delete();
            _entries = [];
        }
    }

    /// <summary>True when saving these would cut either side to <see cref="MaxFieldChars"/>.</summary>
    public static bool WouldCut(string? original, string? rewrite) => (original?.Length ?? 0) > MaxFieldChars || (rewrite?.Length ?? 0) > MaxFieldChars;

    /// <summary>Cuts to <see cref="MaxFieldChars"/> without leaving half of a surrogate pair.</summary>
    internal static string Limit(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (text.Length <= MaxFieldChars) return text;
        var length = char.IsHighSurrogate(text[MaxFieldChars - 1]) ? MaxFieldChars - 1 : MaxFieldChars;
        return text[..length];
    }

    private void EnsureWritable()
    {
        if (LoadOutcome == HistoryLoadOutcome.Unavailable) throw new InvalidOperationException("History is unavailable: the stored file could not be read or set aside.");
    }

    private void Commit(List<HistoryEntry> next)
    {
        _persistence.Write(JsonSerializer.SerializeToUtf8Bytes(new FileModel(CurrentVersion, next!)));
        _entries = next;
    }

    private void Load()
    {
        byte[]? plain;
        try
        {
            plain = _persistence.Read();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LoadOutcome = HistoryLoadOutcome.Unavailable; // maybe locked right now: do not move or overwrite it
            return;
        }
        catch (Exception)
        {
            MoveAside(); // not decryptable (other user, damaged)
            return;
        }

        if (plain is null) { LoadOutcome = HistoryLoadOutcome.Empty; return; }

        List<HistoryEntry>? parsed;
        try { parsed = Parse(plain); }
        catch (NewerHistoryFileException) { LoadOutcome = HistoryLoadOutcome.Unavailable; return; } // newer data: leave it, never overwrite it
        if (parsed is null) { MoveAside(); return; }
        _entries = parsed;
        LoadOutcome = HistoryLoadOutcome.Ok;
    }

    private void MoveAside()
    {
        try
        {
            _persistence.MoveAside();
            LoadOutcome = HistoryLoadOutcome.MovedAside;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LoadOutcome = HistoryLoadOutcome.Unavailable;
        }
    }

    /// <summary>Null when the bytes are not a file this app version understands (corrupt, or Version missing or newer). Entries are cleaned to the limits.</summary>
    private static List<HistoryEntry>? Parse(byte[] plain)
    {
        FileModel? model;
        try { model = JsonSerializer.Deserialize<FileModel>(plain); }
        catch (JsonException) { return null; }
        if (model is { Version: > CurrentVersion }) throw new NewerHistoryFileException();
        // Version 1 is the first format, so anything else is unknown. A migration for older versions goes here when a version 2 exists.
        if (model is not { Version: CurrentVersion, Entries: not null }) return null;

        var seen = new HashSet<string>();
        var clean = new List<HistoryEntry>();
        foreach (var e in model.Entries)
        {
            if (e is null || string.IsNullOrEmpty(e.Id) || string.IsNullOrWhiteSpace(e.Rewrite) || !seen.Add(e.Id)) continue;
            clean.Add(new HistoryEntry(e.Id, e.SavedUtc.ToUniversalTime(), Limit(e.Original), Limit(e.Rewrite)));
            if (clean.Count == MaxEntries) break;
        }
        return clean;
    }
}
