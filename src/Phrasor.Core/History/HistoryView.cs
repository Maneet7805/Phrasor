using System.Globalization;
using System.Text;

namespace Phrasor.Core.History;

public sealed record HistoryItem(string Id, string Time, string Preview, int OriginalChars, int RewriteChars);
public sealed record HistoryGroup(string Label, IReadOnlyList<HistoryItem> Items);

/// <summary>What the History page and Home's Recent list get. Notice is "moved_aside" or "unavailable" when the file had a problem.</summary>
public sealed record HistoryListView(string? Notice, int Total, int Matches, int RetentionDays, IReadOnlyList<HistoryGroup> Groups);

/// <summary>One saved item in full, sent only when the page opens it.</summary>
public sealed record HistoryEntryView(string Id, string Original, string Rewrite);

public static class HistoryView
{
    public const int MaxQueryChars = 200;
    public const int PreviewChars = 160;

    /// <summary>
    /// Searches (every word must appear in the draft or the rewrite, any case), keeps the newest <paramref name="limit"/> matches
    /// (0 = all) and groups them by local day. Entries must already be newest first.
    /// </summary>
    public static HistoryListView Build(IReadOnlyList<HistoryEntry> entries, HistoryLoadOutcome outcome, int retentionDays,
        string query, int limit, DateTime nowUtc, TimeZoneInfo zone)
    {
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var matches = entries.Where(e => words.All(w => Has(e.Original, w) || Has(e.Rewrite, w))).ToList();
        var shown = limit > 0 ? matches.Take(limit) : matches;
        var today = Local(nowUtc, zone).Date;

        var groups = shown
            .Select(e => (Entry: e, Local: Local(e.SavedUtc, zone)))
            .GroupBy(x => x.Local.Date)
            .Select(g => new HistoryGroup(Label(g.Key, today), g.Select(x => new HistoryItem(
                x.Entry.Id, x.Local.ToString("HH:mm", CultureInfo.InvariantCulture), Preview(x.Entry.Rewrite), x.Entry.Original.Length, x.Entry.Rewrite.Length)).ToList()))
            .ToList();

        var notice = outcome switch { HistoryLoadOutcome.MovedAside => "moved_aside", HistoryLoadOutcome.Unavailable => "unavailable", _ => null };
        return new HistoryListView(notice, entries.Count, matches.Count, retentionDays, groups);
    }

    private static bool Has(string text, string word) => text.Contains(word, StringComparison.CurrentCultureIgnoreCase);

    private static DateTime Local(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);

    private static string Label(DateTime day, DateTime today) =>
        day == today ? "Today"
        : day == today.AddDays(-1) ? "Yesterday"
        : day.ToString(day.Year == today.Year ? "dddd d MMMM" : "d MMMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>The rewrite on one line (runs of whitespace become one space), cut at <see cref="PreviewChars"/> with an ellipsis.</summary>
    internal static string Preview(string text)
    {
        var sb = new StringBuilder();
        var space = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
            if (space) { sb.Append(' '); space = false; }
            sb.Append(c);
            if (sb.Length > PreviewChars) break;
        }
        if (sb.Length <= PreviewChars) return sb.ToString();
        var cut = char.IsHighSurrogate(sb[PreviewChars - 1]) ? PreviewChars - 1 : PreviewChars;
        return sb.ToString(0, cut) + "…";
    }
}
