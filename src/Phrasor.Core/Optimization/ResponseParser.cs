using System.Text.Json;

namespace Phrasor.Core.Optimization;

public sealed record ParsedResponse(TaskType TaskType, Verdict Verdict, string Prompt, IReadOnlyList<string> Changes, IReadOnlyList<string> Questions,
    IReadOnlyList<SlotReport>? Slots = null);

public sealed class ResponseFormatException(string message) : Exception(message);

/// <summary>
/// Parses the model's JSON reply. Tolerates the usual deviations (code fences, text around the
/// object, trailing commas) but rejects anything without a usable prompt.
/// </summary>
public static class ResponseParser
{
    private static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static ParsedResponse Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ResponseFormatException("The model returned an empty response.");

        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new ResponseFormatException("The model's response was not JSON.");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(content.AsMemory(start, end - start + 1), Options);
        }
        catch (JsonException ex)
        {
            throw new ResponseFormatException($"The model's JSON could not be parsed: {ex.Message}");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new ResponseFormatException("The model's JSON was not an object.");

            var verdict = ParseVerdict(GetString(root, "verdict"));
            var prompt = GetString(root, "prompt")?.Trim() ?? "";
            if (prompt.Length == 0 && verdict != Verdict.Unchanged)
                throw new ResponseFormatException("The model's response had no prompt.");

            return new ParsedResponse(
                ParseTaskType(GetString(root, "task_type")),
                verdict,
                prompt,
                GetStringArray(root, "changes", max: 4),
                GetStringArray(root, "questions", max: 3),
                GetSlots(root));
        }
    }

    public static TaskType ParseTaskType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "coding" or "code" => TaskType.Coding,
        "debugging" or "debug" => TaskType.Debugging,
        "research" => TaskType.Research,
        "study" or "learning" or "explanation" => TaskType.Study,
        "writing" or "editing" => TaskType.Writing,
        "planning" or "plan" => TaskType.Planning,
        "image" or "image_generation" or "image-generation" => TaskType.Image,
        _ => TaskType.General,
    };

    public static Verdict ParseVerdict(string? value) => value?.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_') switch
    {
        "unchanged" or "no_change" or "none" => Verdict.Unchanged,
        "light_edit" or "light" or "minor" or "minor_edit" => Verdict.LightEdit,
        _ => Verdict.Rewritten,
    };

    /// <summary>One entry per known id, first one wins. Null when there is no array: the model did not report slots.</summary>
    private static IReadOnlyList<SlotReport>? GetSlots(JsonElement root)
    {
        if (!root.TryGetProperty("slots", out var el) || el.ValueKind != JsonValueKind.Array) return null;
        var slots = new List<SlotReport>();
        foreach (var item in el.EnumerateArray().Take(MaxSlotEntries))
        {
            var id = item.ValueKind == JsonValueKind.Object ? GetString(item, "id")?.Trim().ToLowerInvariant() : null;
            if (id is null || !SlotCatalog.IsKnownId(id) || slots.Any(s => s.Id == id)) continue;
            var quote = GetString(item, "quote")?.Trim();
            var filled = string.Equals(GetString(item, "status")?.Trim(), "filled", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(quote);
            slots.Add(new SlotReport(id, filled ? SlotStatus.Filled : SlotStatus.Missing, filled ? quote : null));
        }
        return slots;
    }

    /// <summary>More entries than any slot list has: the rest is noise.</summary>
    private const int MaxSlotEntries = 12;

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static IReadOnlyList<string> GetStringArray(JsonElement root, string name, int max)
    {
        if (!root.TryGetProperty(name, out var el)) return [];
        if (el.ValueKind == JsonValueKind.String)
        {
            var single = el.GetString()?.Trim();
            return string.IsNullOrEmpty(single) ? [] : [single];
        }
        if (el.ValueKind != JsonValueKind.Array) return [];
        return el.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()!.Trim())
            .Where(x => x.Length > 0 && !x.Equals("none", StringComparison.OrdinalIgnoreCase))
            .Take(max)
            .ToList();
    }
}
