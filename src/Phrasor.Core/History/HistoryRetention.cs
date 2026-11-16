namespace Phrasor.Core.History;

public static class HistoryRetention
{
    public static readonly IReadOnlyList<int> Allowed = [0, 30, 90];

    /// <summary>An allowed value stays; anything else becomes 0 (keep until deleted).</summary>
    public static int Normalise(int days) => Allowed.Contains(days) ? days : 0;
}
