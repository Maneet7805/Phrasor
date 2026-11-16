namespace Phrasor.Core.History;

/// <summary>One rewrite the user chose to save. <see cref="Id"/> is random and says nothing about the content. <see cref="SavedUtc"/> is UTC.</summary>
public sealed record HistoryEntry(string Id, DateTime SavedUtc, string Original, string Rewrite);
