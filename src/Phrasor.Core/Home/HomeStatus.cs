using Phrasor.Core.Llm;
using Phrasor.Core.Optimization;
using Phrasor.Core.Settings;

namespace Phrasor.Core.Home;

public enum HomeState { Ready, NeedsKey, Paused, ShortcutProblem }

/// <summary>One shortcut as Home shows it. <see cref="Id"/> is optimize, selection or voice.</summary>
public sealed record ShortcutStatus(string Id, string Label, string Chord, bool Registered);

/// <summary>
/// What the Home page shows, worked out from settings and what the app reports. Holds a yes or no for the key, never the key.
/// </summary>
public sealed record HomeStatus(
    HomeState State, bool Paused, string Provider, string Model, bool KeyRequired, bool KeySet,
    OptimizeMode DefaultMode, IReadOnlyList<ShortcutStatus> Shortcuts)
{
    public const string OptimizeId = "optimize", SelectionId = "selection", VoiceId = "voice";

    /// <param name="paused">Whether the tray's "Pause shortcuts" is on.</param>
    /// <param name="registeredIds">The shortcut ids Windows accepted when the app registered them.</param>
    public static HomeStatus Build(AppSettings settings, ISecretStore secrets, bool paused, IReadOnlySet<string> registeredIds)
    {
        var preset = ProviderPresets.Get(settings.Primary.Provider);
        var keySet = !preset.RequiresKey || !string.IsNullOrWhiteSpace(secrets.Get(preset.Id));

        var shortcuts = new List<ShortcutStatus>();
        void Add(string id, string label, string chord)
        {
            if (!string.IsNullOrWhiteSpace(chord)) shortcuts.Add(new(id, label, chord, registeredIds.Contains(id)));
        }
        Add(OptimizeId, "Rewrite", settings.Hotkeys.Optimize);
        Add(SelectionId, "Selection only", settings.Hotkeys.OptimizeSelection);
        if (settings.Voice.Enabled) Add(VoiceId, "Dictate", settings.Hotkeys.Voice);

        var state = paused ? HomeState.Paused
            : !keySet ? HomeState.NeedsKey
            : shortcuts.Any(s => !s.Registered) ? HomeState.ShortcutProblem
            : HomeState.Ready;
        return new(state, paused, preset.DisplayName, settings.Primary.Model, preset.RequiresKey, keySet, settings.DefaultMode, shortcuts);
    }
}
