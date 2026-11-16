using Phrasor.Core.Optimization;

namespace Phrasor.App.UI.Main;

/// <summary>
/// What the main window borrows from the tray app: the shortcut pause (the tray's own item), which shortcuts are registered,
/// the optimiser call the hotkey uses, and the shortcut registration the Settings page needs. Nothing here captures, pastes or sends keys.
/// </summary>
/// <param name="SetRecording">True releases every global shortcut so a pressed chord reaches the page; false puts them back (unless paused).</param>
/// <param name="ProbeShortcut">A plain message when another app holds the chord, or null when it is free (or already ours).</param>
/// <param name="ShortcutWarning">A plain warning about a chord that is allowed but costly (Ctrl+Alt can be AltGr), or null.</param>
/// <param name="ApplyShortcuts">Registers the shortcuts again from the live settings, after a save.</param>
internal sealed record MainHooks(
    Func<bool> IsPaused,
    Action<bool> SetPaused,
    Func<IReadOnlySet<string>> RegisteredShortcuts,
    Func<string, OptimizeMode, TaskType, CancellationToken, Task<OptimizeResult>> Optimize,
    Action<bool> SetRecording,
    Func<string, string?> ProbeShortcut,
    Func<string, string?> ShortcutWarning,
    Action ApplyShortcuts);
