using System.Text.Json;
using Phrasor.Core.History;
using System.Text.Json.Serialization;
using Phrasor.Core.Llm;
using Phrasor.Core.Optimization;
using Phrasor.Core.Theme;

namespace Phrasor.Core.Settings;

public sealed class EndpointSettings
{
    public string Provider { get; set; } = ProviderPresets.Groq.Id;
    public string Model { get; set; } = ProviderPresets.Groq.DefaultModel;

    /// <summary>Only used by the "custom" and "ollama" providers; others use the preset URL.</summary>
    public string? BaseUrl { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Key-store id of a key kept just for this model. Null means the provider's key is used.</summary>
    public string? KeyId { get; set; }
}

/// <summary>A model the user saved to switch between. One of them is the default; others can be the backup chain.</summary>
public sealed class SavedModel
{
    public string Id { get; set; } = "";
    public string Provider { get; set; } = ProviderPresets.Groq.Id;
    public string Model { get; set; } = "";

    /// <summary>Only used by the "custom" and "ollama" providers.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>True when this model has its own API key. False reuses the provider's key.</summary>
    public bool SeparateKey { get; set; }

    [JsonIgnore] public string? KeyId => SeparateKey ? ModelList.KeyIdFor(Id) : null;
    [JsonIgnore] public bool IsLocal => ModelList.IsLocal(Provider, BaseUrl);
}

/// <summary>How the review window looks and starts. Defaults suit a fresh install and an old settings file alike.</summary>
public sealed class ReviewWindowSettings
{
    public const int MinBarCount = 4, MaxBarCount = 10, DefaultBarCount = 6;

    /// <summary>Task chips after Auto, in the order shown. Auto is always first and is not listed.</summary>
    public List<string> TaskOrder { get; set; } = [.. PreferredOrder];

    /// <summary>How many chips show in the bar before "More", counting Auto.</summary>
    public int BarCount { get; set; } = DefaultBarCount;

    public string DefaultTask { get; set; } = nameof(TaskType.Auto);
    public bool HighlightChanges { get; set; }

    /// <summary>Start with the task and mode the user last chose, instead of the defaults. Only those two words are kept.</summary>
    public bool RememberLastChoice { get; set; }
    public string LastTask { get; set; } = nameof(TaskType.Auto);
    public string LastMode { get; set; } = nameof(OptimizeMode.Standard);

    /// <summary>After the first result, quietly write the other two modes. Three times the requests, so off by default.</summary>
    public bool PrefetchOtherModes { get; set; }

    internal static readonly string[] PreferredOrder =
        [nameof(TaskType.Coding), nameof(TaskType.Writing), nameof(TaskType.Research), nameof(TaskType.Image), nameof(TaskType.Video),
         nameof(TaskType.Debugging), nameof(TaskType.Study), nameof(TaskType.Planning), nameof(TaskType.General)];

    /// <summary>Repairs a hand-edited or older file: every task listed once, counts in range, names valid.</summary>
    public void Normalise()
    {
        var all = PreferredOrder.Concat(Enum.GetNames<TaskType>().Where(n => n != nameof(TaskType.Auto))).Distinct().ToList();
        var order = new List<string>();
        foreach (var name in TaskOrder ?? [])
        {
            var match = all.FirstOrDefault(a => a.Equals(name?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is not null && !order.Contains(match)) order.Add(match);
        }
        order.AddRange(all.Where(a => !order.Contains(a)));
        TaskOrder = order;
        BarCount = Math.Clamp(BarCount, MinBarCount, MaxBarCount);
        DefaultTask = Canonical(DefaultTask, nameof(TaskType.Auto));
        LastTask = Canonical(LastTask, nameof(TaskType.Auto));
        LastMode = Enum.TryParse(LastMode, ignoreCase: true, out OptimizeMode mode) && Enum.IsDefined(mode) ? mode.ToString() : nameof(OptimizeMode.Standard);
    }

    private static string Canonical(string? value, string fallback) =>
        Enum.GetNames<TaskType>().FirstOrDefault(n => n.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? fallback;
}

public sealed class HotkeySettings
{
    /// <summary>Selects all text in the focused field, optimises it, replaces it on accept.</summary>
    public string Optimize { get; set; } = "Ctrl+Alt+P";

    /// <summary>Optimises only the current selection; for documents and editors where select-all is too much.</summary>
    public string OptimizeSelection { get; set; } = "Ctrl+Alt+Shift+P";

    /// <summary>Tap to start/stop dictation, or hold to talk and release to finish.</summary>
    public string Voice { get; set; } = "Ctrl+Alt+D";
}

public sealed class VoiceSettings
{
    public bool Enabled { get; set; } = true;
    public string Provider { get; set; } = ProviderPresets.Groq.Id;
    public string Model { get; set; } = ProviderPresets.Groq.DefaultTranscriptionModel!;
    public string? BaseUrl { get; set; }

    /// <summary>ISO-639-1 code such as "en"; empty lets the model detect the language.</summary>
    public string Language { get; set; } = "";

    /// <summary>Send the transcript straight to the optimiser and review it, instead of typing it in.</summary>
    public bool AutoOptimize { get; set; }

    public int MaxSeconds { get; set; } = 120;
}

/// <summary>Reads a theme name; anything unknown or of the wrong type becomes System instead of failing the whole file.</summary>
public sealed class LenientThemeModeConverter : JsonConverter<ThemeMode>
{
    public override ThemeMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        reader.Skip();
        return Enum.TryParse(value, ignoreCase: true, out ThemeMode mode) && Enum.IsDefined(mode) ? mode : ThemeMode.System;
    }

    public override void Write(Utf8JsonWriter writer, ThemeMode value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

public sealed class UiSettings
{
    [JsonConverter(typeof(LenientThemeModeConverter))]
    public ThemeMode Theme { get; set; } = ThemeMode.System;
}

public sealed class HistorySettings
{
    /// <summary>Saved items older than this many days are removed. 0 keeps them until deleted. Only 0, 30 and 90 are accepted.</summary>
    public int RetentionDays { get; set; }
}

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public EndpointSettings Primary { get; set; } = new();
    public EndpointSettings Fallback { get; set; } = new() { Model = ProviderPresets.Groq.DefaultFallbackModel };

    /// <summary>
    /// The models the user can switch between. <see cref="Primary"/> and <see cref="Fallback"/> mirror the default model and the first
    /// backup, so older code and old settings files keep working; <see cref="ModelList.Normalise"/> keeps the two in step.
    /// </summary>
    public List<SavedModel> SavedModels { get; set; } = [];
    public string DefaultModelId { get; set; } = "";

    /// <summary>Models tried, in this order, when the default cannot answer.</summary>
    public List<string> BackupModelIds { get; set; } = [];
    public ReviewWindowSettings ReviewWindow { get; set; } = new();

    /// <summary>
    /// The model that grades rewrites in the quality check. A different provider from the optimiser,
    /// so the judge isn't grading its own model family. Uses the API key saved for that provider.
    /// </summary>
    public EndpointSettings Judge { get; set; } = new() { Provider = ProviderPresets.Gemini.Id, Model = ProviderPresets.Gemini.DefaultModel };
    public OptimizeMode DefaultMode { get; set; } = OptimizeMode.Standard;

    /// <summary>When false, accepted results replace the text immediately (still never sends it).</summary>
    public bool ReviewBeforeReplace { get; set; } = true;

    /// <summary>Optional standing background, e.g. "Year 3 CS student; I mostly use C#, Java and Python."</summary>
    public string PersonalContext { get; set; } = "";

    /// <summary>
    /// The user's own requirements per task type, keyed by <see cref="TaskType"/> name (e.g. "Coding").
    /// Added word for word to rewrites of that type; never sent to the model.
    /// </summary>
    public Dictionary<string, string> StandingRequirements { get; set; } = [];

    /// <summary>Tell the model which kind of app the draft came from (e.g. "Claude (browser)").</summary>
    public bool SendAppHint { get; set; } = true;

    public bool RepairDroppedDetails { get; set; } = true;

    /// <summary>Check GitHub Releases for a newer version once a day. Sends no prompt text, settings or keys.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>When the last automatic update check ran, so restarts don't check again within a day.</summary>
    public DateTime? LastUpdateCheckUtc { get; set; }
    public int TimeoutSeconds { get; set; } = 45;
    public HotkeySettings Hotkeys { get; set; } = new();
    public VoiceSettings Voice { get; set; } = new();
    public UiSettings Ui { get; set; } = new();
    public HistorySettings History { get; set; } = new();
    public bool FirstRunComplete { get; set; }
}

public static class SettingsStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Loads settings; a missing file gives defaults, a corrupt one is kept aside as .bad and replaced by defaults.</summary>
    public static AppSettings Load(string path, Action<string>? log = null)
    {
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
            settings.Primary ??= new EndpointSettings();
            settings.Fallback ??= new EndpointSettings { Enabled = false };
            settings.Hotkeys ??= new HotkeySettings();
            settings.Voice ??= new VoiceSettings();
            settings.Ui ??= new UiSettings();
            settings.History ??= new HistorySettings();
            settings.History.RetentionDays = HistoryRetention.Normalise(settings.History.RetentionDays);
            settings.Judge ??= new AppSettings().Judge;
            settings.StandingRequirements ??= [];
            settings.ReviewWindow ??= new ReviewWindowSettings();
            settings.ReviewWindow.Normalise();
            ModelList.Normalise(settings);
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            log?.Invoke($"settings file unreadable ({ex.GetType().Name}); using defaults");
            try { File.Copy(path, path + ".bad", overwrite: true); } catch (IOException) { }
            return new AppSettings();
        }
    }

    /// <summary>An independent copy, so a change can be checked and applied as a whole or dropped.</summary>
    public static AppSettings Clone(AppSettings s) =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s, JsonOptions), JsonOptions)!;

    public static void Save(string path, AppSettings settings)
    {
        ModelList.Normalise(settings);
        settings.ReviewWindow ??= new ReviewWindowSettings();
        settings.ReviewWindow.Normalise();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }
}
