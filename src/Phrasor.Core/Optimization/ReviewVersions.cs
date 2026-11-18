namespace Phrasor.Core.Optimization;

/// <summary>Which version of a rewrite: the chip pressed (Auto is its own value), the mode and the model.</summary>
public readonly record struct VersionKey(TaskType Task, OptimizeMode Mode, string ModelId);

/// <summary>
/// Rewrites made during one review window, kept in memory so going back to a version shows the identical text
/// with no model call. Every version is made from the same original draft, never from another version, so
/// versions cannot drift. Nothing here is saved, logged or printed: it is gone when the window closes.
/// </summary>
public sealed class ReviewVersions
{
    private readonly object _gate = new();
    private readonly string _original;
    private readonly Dictionary<VersionKey, OptimizeResult> _results = [];
    private readonly Dictionary<VersionKey, string> _edits = [];
    private readonly List<VersionKey> _order = [];

    public ReviewVersions(string original)
    {
        ArgumentNullException.ThrowIfNull(original);
        _original = original;
    }

    public int Count { get { lock (_gate) return _results.Count; } }

    /// <summary>Keys in the order their versions were written (for the "already written" dots).</summary>
    public IReadOnlyList<VersionKey> Keys { get { lock (_gate) return [.. _order]; } }

    public bool Has(VersionKey key) { lock (_gate) return _results.ContainsKey(key); }

    public bool TryGet(VersionKey key, out OptimizeResult result)
    {
        lock (_gate) return _results.TryGetValue(key, out result!);
    }

    /// <summary>
    /// Stores a version, replacing the one under the same key ("Rewrite again") and dropping its edit.
    /// Only a result made from the original draft is accepted.
    /// </summary>
    public void Put(VersionKey key, OptimizeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!string.Equals(result.Original, _original, StringComparison.Ordinal))
            throw new ArgumentException("A version must be made from the original draft.", nameof(result));
        lock (_gate)
        {
            if (!_results.ContainsKey(key)) _order.Add(key);
            _results[key] = result;
            _edits.Remove(key);
        }
    }

    /// <summary>The user's edited text for a version, or null when they have not edited it.</summary>
    public string? GetEdit(VersionKey key)
    {
        lock (_gate) return _edits.GetValueOrDefault(key);
    }

    /// <summary>Keeps the user's edit with its version. Ignored for a version that does not exist.</summary>
    public void SetEdit(VersionKey key, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        lock (_gate) { if (_results.ContainsKey(key)) _edits[key] = text; }
    }

    public void ClearEdit(VersionKey key) { lock (_gate) _edits.Remove(key); }

    /// <summary>Forgets every version. Used when the user adds details, because the input changed.</summary>
    public void Clear()
    {
        lock (_gate) { _results.Clear(); _edits.Clear(); _order.Clear(); }
    }

    public override string ToString() => $"ReviewVersions({Count})";
}
