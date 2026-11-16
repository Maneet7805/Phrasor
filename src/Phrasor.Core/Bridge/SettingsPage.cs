using System.Diagnostics;
using Phrasor.Core.Home;
using Phrasor.Core.Llm;
using Phrasor.Core.Optimization;
using Phrasor.Core.Settings;
using Phrasor.Core.Theme;

namespace Phrasor.Core.Bridge;

public sealed record EndpointDraft(string Provider, string Model, string? BaseUrl, bool Enabled);

/// <summary>The three chords as the recorder wrote them. An empty string means no shortcut.</summary>
public sealed record ShortcutsDraft(string Optimize, string OptimizeSelection, string Voice);

public sealed record VoiceDraft(bool Enabled, string Provider, string Model, string Language, bool AutoOptimize, int MaxSeconds);

/// <summary>
/// What the Settings page sends on Save and on Test connection. Keys appear only in <see cref="NewKeys"/> (typed this time)
/// and <see cref="RemoveKeys"/> (ids to forget); the app never sends a saved key back.
/// </summary>
public sealed record SettingsDraft(
    ThemeMode Theme, OptimizeMode DefaultMode, bool ReviewBeforeReplace, bool StartWithWindows, bool SendAppHint,
    bool RepairDroppedDetails, bool CheckForUpdates, string PersonalContext,
    EndpointDraft Primary, EndpointDraft Fallback, ShortcutsDraft Shortcuts, VoiceDraft Voice,
    IReadOnlyDictionary<string, string> Requirements,
    IReadOnlyDictionary<string, string> NewKeys, IReadOnlySet<string> RemoveKeys);

public sealed record GeneralView(string Theme, string DefaultMode, bool ReviewBeforeReplace, bool StartWithWindows, bool SendAppHint,
    bool RepairDroppedDetails, bool CheckForUpdates, string PersonalContext);

public sealed record EndpointView(string Provider, string Model, string BaseUrl, bool Enabled);

/// <summary>One API key row. <see cref="Set"/> is all the page ever learns about a key.</summary>
public sealed record KeyView(string Id, string Name, bool Set, string? LinkId);

public sealed record ProviderView(string Id, string Name, string DefaultModel, string DefaultFallbackModel, string Notes, bool UrlEditable, bool RequiresKey);

public sealed record ShortcutsView(string Optimize, string OptimizeSelection, string Voice);

public sealed record VoiceView(bool Enabled, string Provider, string Model, string Language, bool AutoOptimize, int MaxSeconds);

public sealed record NamedOption(string Id, string Name);

/// <summary>One standing-requirements box: the text for a kind of task (or for all tasks).</summary>
public sealed record RequirementView(string Id, string Label, string Text);

public sealed record SettingsView(GeneralView General, EndpointView Primary, EndpointView Fallback,
    IReadOnlyList<KeyView> Keys, IReadOnlyList<ProviderView> Providers, IReadOnlyList<string> Modes,
    ShortcutsView Shortcuts, VoiceView Voice, IReadOnlyList<NamedOption> VoiceProviders,
    IReadOnlyList<RequirementView> Requirements, int RequirementsMax);

/// <summary>A plain message about one shortcut. <see cref="Id"/> is optimize, selection or voice.</summary>
public sealed record ShortcutNote(string Id, string Message);

/// <summary>
/// The result of Save. When <see cref="Problems"/> is not empty nothing was saved and <see cref="Settings"/> is what the app still has.
/// <see cref="Warnings"/> do not stop a save.
/// </summary>
public sealed record SaveOutcome(bool Saved, SettingsView Settings, IReadOnlyList<ShortcutNote> Problems, IReadOnlyList<ShortcutNote> Warnings);

/// <summary>The outcome of Test connection. Only a fixed error kind, never provider text.</summary>
public sealed record ConnectionResult(bool Ok, string? Label, long Milliseconds, string? Error);

/// <summary>Builds what the Settings page may see, applies what it sends, and runs Test connection. No Windows APIs.</summary>
public static class SettingsPage
{
    /// <summary>Providers that take an API key on the page, in display order. Ollama needs none.</summary>
    public static readonly IReadOnlyList<string> KeyProviders = ["groq", "gemini", "custom"];

    private static readonly Dictionary<string, string> KeyLinks = new() { ["groq"] = "groq-keys", ["gemini"] = "gemini-keys" };

    public static SettingsView BuildView(AppSettings s, ISecretStore secrets, bool startWithWindows) => new(
        new GeneralView(s.Ui.Theme.ToString().ToLowerInvariant(), s.DefaultMode.ToString(), s.ReviewBeforeReplace, startWithWindows,
            s.SendAppHint, s.RepairDroppedDetails, s.CheckForUpdates, s.PersonalContext),
        EndpointOf(s.Primary), EndpointOf(s.Fallback),
        [.. KeyProviders.Select(id =>
        {
            var p = ProviderPresets.Get(id);
            return new KeyView(id, p.DisplayName, !string.IsNullOrWhiteSpace(secrets.Get(id)), KeyLinks.GetValueOrDefault(id));
        })],
        [.. ProviderPresets.All.Select(p => new ProviderView(p.Id, p.DisplayName, p.DefaultModel, p.DefaultFallbackModel, p.Notes, IsUrlProvider(p.Id), p.RequiresKey))],
        Enum.GetNames<OptimizeMode>(),
        new ShortcutsView(s.Hotkeys.Optimize, s.Hotkeys.OptimizeSelection, s.Hotkeys.Voice),
        new VoiceView(s.Voice.Enabled, s.Voice.Provider == "custom" ? "custom" : "groq", s.Voice.Model, s.Voice.Language,
            s.Voice.AutoOptimize, Math.Clamp(s.Voice.MaxSeconds, VoiceMinSeconds, VoiceMaxSeconds)),
        [.. VoiceProviderIds.Select(id => new NamedOption(id, ProviderPresets.Get(id).DisplayName))],
        [.. RequirementIds.Select(id => new RequirementView(id, id == RequirementsAppender.AllTasksKey ? "All tasks" : id,
            s.StandingRequirements.FirstOrDefault(kv => string.Equals(kv.Key, id, StringComparison.OrdinalIgnoreCase)).Value ?? ""))],
        RequirementsAppender.MaxLength);

    public const int VoiceMinSeconds = 10, VoiceMaxSeconds = 600;

    /// <summary>Voice providers the page offers: Groq's Whisper, or any OpenAI-compatible address.</summary>
    public static readonly IReadOnlyList<string> VoiceProviderIds = ["groq", "custom"];

    /// <summary>Requirement boxes in display order: all tasks, then every task type except Auto.</summary>
    public static readonly IReadOnlyList<string> RequirementIds =
        [RequirementsAppender.AllTasksKey, .. Enum.GetNames<TaskType>().Where(n => n != nameof(TaskType.Auto))];

    internal static bool IsUrlProvider(string id) => id is "custom" or "ollama";

    private static EndpointView EndpointOf(EndpointSettings e) => new(ProviderPresets.Get(e.Provider).Id, e.Model, e.BaseUrl ?? "", e.Enabled);

    /// <summary>Writes the page's choices onto <paramref name="s"/>. Keys are not touched here; the app stores them.</summary>
    public static void Apply(AppSettings s, SettingsDraft d)
    {
        s.Ui.Theme = d.Theme;
        s.DefaultMode = d.DefaultMode;
        s.ReviewBeforeReplace = d.ReviewBeforeReplace;
        s.SendAppHint = d.SendAppHint;
        s.RepairDroppedDetails = d.RepairDroppedDetails;
        s.CheckForUpdates = d.CheckForUpdates;
        s.PersonalContext = d.PersonalContext;
        s.Primary = new EndpointSettings { Provider = d.Primary.Provider, Model = d.Primary.Model, BaseUrl = d.Primary.BaseUrl };
        s.Fallback = new EndpointSettings { Provider = d.Fallback.Provider, Model = d.Fallback.Model, BaseUrl = d.Fallback.BaseUrl, Enabled = d.Fallback.Enabled };
        s.Hotkeys = new HotkeySettings
        {
            Optimize = Canonical(d.Shortcuts.Optimize), OptimizeSelection = Canonical(d.Shortcuts.OptimizeSelection), Voice = Canonical(d.Shortcuts.Voice),
        };
        // As in the classic window, a custom voice endpoint uses the main model's address.
        s.Voice = new VoiceSettings
        {
            Enabled = d.Voice.Enabled, Provider = d.Voice.Provider, Model = d.Voice.Model, Language = d.Voice.Language,
            BaseUrl = d.Voice.Provider == "custom" ? d.Primary.BaseUrl : null, AutoOptimize = d.Voice.AutoOptimize, MaxSeconds = d.Voice.MaxSeconds,
        };
        s.StandingRequirements = d.Requirements.Where(kv => kv.Value.Length > 0).ToDictionary(kv => kv.Key, kv => kv.Value);
        s.FirstRunComplete = true;
    }

    private static string Canonical(string text) => HotkeyChord.TryParse(text, out var chord) ? chord.ToString() : text.Trim();

    /// <summary>
    /// Plain messages for chords that cannot be used: unreadable, no Ctrl or Alt, a chord given to two actions, or (through
    /// <paramref name="probe"/>, which returns a message or null) one another app already holds. Empty means no problem.
    /// The voice chord is ignored while voice is off, as it is not registered then.
    /// </summary>
    public static IReadOnlyList<ShortcutNote> CheckShortcuts(ShortcutsDraft d, bool voiceOn, Func<string, string?> probe)
    {
        var notes = new List<ShortcutNote>();
        var seen = new Dictionary<string, string>();
        var items = new List<(string Id, string Label, string Text)>
        {
            (HomeStatus.OptimizeId, "Rewrite", d.Optimize), (HomeStatus.SelectionId, "Selection only", d.OptimizeSelection),
        };
        if (voiceOn) items.Add((HomeStatus.VoiceId, "Dictate", d.Voice));
        foreach (var (id, label, text) in items.Where(i => !string.IsNullOrWhiteSpace(i.Text)))
        {
            if (!HotkeyChord.TryParse(text, out var chord)) { notes.Add(new(id, $"{label}: \"{text.Trim()}\" isn't a valid shortcut.")); continue; }
            if (chord.Problem() is { } problem) { notes.Add(new(id, $"{label}: {problem}")); continue; }
            if (!seen.TryAdd(chord.ToString(), label)) { notes.Add(new(id, $"{label}: {chord} is already used for {seen[chord.ToString()]}.")); continue; }
            if (probe(chord.ToString()) is { } taken) notes.Add(new(id, $"{label}: {taken}"));
        }
        return notes;
    }

    /// <summary>
    /// Asks the main model for one word. Failures become a fixed kind; the provider's own message is never read,
    /// so it cannot reach the page or the log.
    /// </summary>
    public static async Task<ConnectionResult> TestAsync(ILlmClient client, AppSettings s, ISecretStore secrets, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var cfg = s.Primary;
            if (string.IsNullOrWhiteSpace(cfg.Model)) return new(false, null, 0, TryItErrors.NoModel);
            var endpoint = EndpointResolver.Build(cfg.Provider, cfg.Model, cfg.BaseUrl, secrets, out var keyProblem);
            if (endpoint is null) return new(false, null, 0, keyProblem is null ? TryItErrors.NoModel : TryItErrors.NoKey);
            await client.CompleteAsync(endpoint, [new ChatMessage("user", "Reply with the single word OK.")],
                new ChatOptions { MaxTokens = 300, JsonMode = false, Mode = OptimizeMode.Light, Timeout = TimeSpan.FromSeconds(20) }, ct);
            return new(true, endpoint.Label, clock.ElapsedMilliseconds, null);
        }
        catch (Exception ex) when (ex is LlmException or OperationCanceledException or InvalidOperationException)
        {
            return new(false, null, clock.ElapsedMilliseconds, TryItErrors.KindOf(ex));
        }
    }
}

/// <summary>Typed keys laid over the saved ones, for a test that must not save anything. Read-only.</summary>
public sealed class OverlaySecretStore(ISecretStore inner, IReadOnlyDictionary<string, string> typed) : ISecretStore
{
    public string? Get(string providerId) => typed.TryGetValue(providerId, out var v) ? v : inner.Get(providerId);
    public void Set(string providerId, string? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
}
