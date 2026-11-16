using Phrasor.Core.Optimization;

namespace Phrasor.App.UI.Main;

/// <summary>
/// What the main window borrows from the tray app: the shortcut pause (the tray's own item), which shortcuts are registered,
/// and the optimiser call the hotkey uses. Nothing here captures, pastes or sends keys.
/// </summary>
internal sealed record MainHooks(
    Func<bool> IsPaused,
    Action<bool> SetPaused,
    Func<IReadOnlySet<string>> RegisteredShortcuts,
    Func<string, OptimizeMode, TaskType, CancellationToken, Task<OptimizeResult>> Optimize);
