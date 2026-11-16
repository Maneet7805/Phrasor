using Phrasor.Core.Home;
using Phrasor.Core.Theme;

namespace Phrasor.Core.Bridge;

public enum WindowAction { Minimize, ToggleMaximize, Close }

/// <summary>What the page sees of the app. Never holds keys or text.</summary>
public sealed record MainWindowState(ThemeMode Theme, bool Dark, bool Maximised);

/// <summary>
/// Everything the page may ask the app to do. Keep it small on purpose: there is no way here to type, paste, submit,
/// read a key or reach another window, so a handler cannot do those without a visible change to this interface.
/// </summary>
public interface IMainWindowApi
{
    MainWindowState GetState();

    /// <summary>Saves the theme setting and applies it.</summary>
    void SetTheme(ThemeMode mode);

    void Window(WindowAction action);

    /// <summary>Opens an allow-listed link (see <see cref="LinkAllowList"/>) in the user's default browser.</summary>
    void OpenLink(Uri url);

    HomeStatus GetHome();

    /// <summary>Turns the shortcuts' pause on or off, as the tray item does. Returns the state after the change.</summary>
    bool SetPaused(bool paused);

    /// <summary>
    /// Starts one rewrite with the same pipeline as the hotkey and returns its run number, or null when a run is going.
    /// The result is pushed later (<see cref="BridgeEvents"/>). It is shown on the page only: never pasted, saved or sent on.
    /// </summary>
    int? StartTryIt(TryItRequest request);

    void CancelTryIt();

    /// <summary>Copies the last rewrite the app produced. The page cannot supply the text. False when there is none.</summary>
    bool CopyRewrite();
}
