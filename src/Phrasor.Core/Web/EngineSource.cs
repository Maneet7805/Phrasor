namespace Phrasor.Core.Web;

/// <summary>
/// Which browser engine the main window runs on: the shared Evergreen runtime (the default) or one folder.
/// A folder is a developer switch (an environment variable), never a user setting.
/// </summary>
public sealed class EngineSource
{
    public static EngineSource Shared { get; } = new(null);

    private EngineSource(string? folder) => BrowserFolder = folder;

    /// <summary>The value for <c>browserExecutableFolder</c>; null means the shared runtime.</summary>
    public string? BrowserFolder { get; }

    /// <summary>Throws <see cref="ArgumentException"/> for an empty, relative or network path (a Fixed Version cannot run from the network).</summary>
    // shortcut: a mapped network drive looks local here; the App checks the drive type too.
    public static EngineSource Folder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("The engine folder is empty.", nameof(path));
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            throw new ArgumentException("The engine folder is a network path.", nameof(path));
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("The engine folder is not a full path.", nameof(path));
        return new EngineSource(path);
    }

    /// <summary>Blank means <see cref="Shared"/>; anything else must be a valid folder (see <see cref="Folder"/>).</summary>
    public static EngineSource FromSetting(string? value) => string.IsNullOrWhiteSpace(value) ? Shared : Folder(value);
}
