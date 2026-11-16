using Phrasor.Core.History;
using Phrasor.Core.Home;
using Phrasor.Core.Theme;

namespace Phrasor.Core.Bridge;

public enum WindowAction { Minimize, ToggleMaximize, Close }

/// <summary>The two folders the Data group may open. The page never names a path.</summary>
public enum DataFolder { Data, Logs }

public enum HistorySaveResult { Saved, AlreadySaved, NothingToSave }

/// <summary>History could not be read or written (the file is locked, damaged or from a newer Phrasor). Carries no text.</summary>
public sealed class HistoryUnavailableException : Exception;

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

    /// <summary>What the Settings page may show. Keys appear only as set or not set.</summary>
    SettingsView GetSettings();

    /// <summary>
    /// Checks the shortcuts, then stores the page's choices and typed keys and re-registers the shortcuts. A shortcut problem means
    /// nothing was saved (<see cref="SaveOutcome.Problems"/>). Throws if the settings could not be written.
    /// </summary>
    SaveOutcome SaveSettings(SettingsDraft draft);

    /// <summary>
    /// True while the page records a chord: the app's global shortcuts are released so a pressed chord reaches the page instead of
    /// running. False puts them back (unless paused). Always safe to call again; the window closing also puts them back.
    /// </summary>
    void SetShortcutRecording(bool recording);

    /// <summary>
    /// Tests the main model with the draft (typed keys included) without saving anything. Returns a run number, or null when a
    /// test is going. The result is pushed later (<see cref="BridgeEvents"/>).
    /// </summary>
    int? StartConnectionTest(SettingsDraft draft);

    /// <summary>
    /// First run: tests the typed key with the provider's default model without saving anything. Returns a run number, or null when a
    /// test is going. The result is pushed like <see cref="StartConnectionTest"/>'s.
    /// </summary>
    int? StartFirstRunTest(FirstRunKey key);

    /// <summary>First run: stores the key and makes the provider the main model. Throws if the settings could not be written.</summary>
    void SaveFirstRunKey(FirstRunKey key);

    /// <summary>Saved items for the History page (limit 0 = all) or Home's Recent list. Removes items past the retention first.</summary>
    HistoryListView ListHistory(string query, int limit);

    /// <summary>One saved item in full, or null when the id is not there.</summary>
    HistoryEntryView? GetHistory(string id);

    /// <summary>
    /// Saves the last Try-it draft and rewrite the app holds. The page never supplies the text. Saving is only ever on this request.
    /// Throws <see cref="HistoryUnavailableException"/> when the file cannot be written.
    /// </summary>
    HistorySaveResult SaveHistory();

    /// <summary>Copies the draft or the rewrite of a saved item, in the app. False when the id is not there.</summary>
    bool CopyHistory(string id, bool original);

    bool DeleteHistory(string id);

    /// <summary>Deletes every saved item. The page asks the user to confirm first.</summary>
    void DeleteAllHistory();

    /// <summary>Sets how long saved items are kept (0, 30 or 90 days; anything else is refused by the bridge) and applies it now.</summary>
    void SetHistoryRetention(int days);

    /// <summary>Opens the settings folder or the logs folder in Explorer.</summary>
    void OpenDataFolder(DataFolder folder);

    /// <summary>
    /// Deletes the settings, saved API keys, logs and saved History, turns off Start with Windows, then closes Phrasor.
    /// Returns false (and keeps running) when something could not be deleted. The page asks the user to confirm first.
    /// </summary>
    bool DeleteAllData();
}
