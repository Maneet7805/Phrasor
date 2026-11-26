using System.Security.Cryptography;
using System.Text;

namespace Phrasor.Core.Install;

/// <summary>
/// Where Phrasor keeps its data, and the machine-wide names it uses to find its own running copy.
/// Normally these are the real folders. <c>--data-dir &lt;folder&gt;</c> moves everything under one test
/// root, so a test copy cannot touch the installed copy's data. A bad value is an error, never a silent
/// fallback to the real folders.
/// </summary>
public sealed record DataRoot
{
    public const string Switch = "--data-dir";
    private const string MutexBase = @"Local\Phrasor.SingleInstance";
    private const string ShowSettingsBase = @"Local\Phrasor.ShowSettings";
    private const string ExitBase = @"Local\Phrasor.Exit";
    private const string WebBase = "Phrasor-web";
    private const int HashHexLength = 8;

    // These take their own arguments and are not app starts, so the switch means nothing to them.
    private static readonly string[] ModeSwitches = ["--eval", "--eval-compare", "--rejudge"];

    private static readonly char[] BadChars = ['"', '<', '>', '|', '?', '*'];

    /// <summary>CON, PRN, AUX, NUL, COM0-9 and LPT0-9, with or without an extension ("com1.txt" is still the port).</summary>
    private static bool IsReservedName(string name)
    {
        var stem = name.Split('.')[0].TrimEnd();
        return stem.ToUpperInvariant() is "CON" or "PRN" or "AUX" or "NUL"
            || (stem.Length == 4 && stem[3] is >= '0' and <= '9' && stem[..3].ToUpperInvariant() is "COM" or "LPT");
    }

    public bool Ok => Error is null;
    public string? Error { get; init; }
    public bool IsOverridden { get; init; }
    public required string DataDir { get; init; }
    /// <summary>Pre-rename data folder; read once by the migration, never written.</summary>
    public required string LegacyDataDir { get; init; }
    public required string HistoryDir { get; init; }
    public required string MutexName { get; init; }
    public required string ShowSettingsEventName { get; init; }
    public required string ExitEventName { get; init; }
    /// <summary>Folder name under the temp folder for the browser engine's files.</summary>
    public required string WebFolderName { get; init; }

    /// <param name="args">The command line without the exe name.</param>
    public static DataRoot Resolve(IReadOnlyList<string> args, string appData, string localAppData, string installDir)
    {
        var real = Real(appData, localAppData);
        if (args.Count > 0 && ModeSwitches.Contains(args[0])) return real;

        string? value = null;
        var seen = 0;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == Switch)
            {
                seen++;
                var next = i + 1 < args.Count ? args[i + 1] : null;
                value = next is null || next.StartsWith("--", StringComparison.Ordinal) ? "" : next;
                if (next is not null && value.Length > 0) i++; // the value is not scanned again
            }
            else if (args[i].StartsWith(Switch + "=", StringComparison.Ordinal))
            {
                seen++;
                value = args[i][(Switch.Length + 1)..];
            }
        }
        if (seen == 0) return real;
        if (seen > 1) return Fail(real, "--data-dir was given more than once.");
        if (string.IsNullOrWhiteSpace(value)) return Fail(real, "--data-dir needs a folder, for example --data-dir D:\\scratch.");
        return FromRoot(real, value, appData, localAppData, installDir);
    }

    private static DataRoot FromRoot(DataRoot real, string value, string appData, string localAppData, string installDir)
    {
        if (value.StartsWith(@"\\", StringComparison.Ordinal) || value.StartsWith("//", StringComparison.Ordinal))
            return Fail(real, "--data-dir cannot be a network or device path.");
        if (!Path.IsPathFullyQualified(value))
            return Fail(real, "--data-dir must be a full path, such as D:\\scratch.");

        string root;
        try { root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { return Fail(real, "--data-dir is not a valid folder path."); }

        // Windows would accept none of these as a folder name, and creating the folder would throw later.
        // (PowerShell 5.1 turns "D:\scratch\" into D:\scratch" and so lands here.)
        var rest = root[(Path.GetPathRoot(root)?.Length ?? 0)..];
        if (rest.IndexOfAny(BadChars) >= 0 || rest.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(IsReservedName))
            return Fail(real, "--data-dir is not a valid folder path.");

        var driveRoot = Path.GetPathRoot(root);
        if (driveRoot is null || Path.TrimEndingDirectorySeparator(driveRoot).Equals(root, StringComparison.OrdinalIgnoreCase))
            return Fail(real, "--data-dir cannot be a drive root.");

        foreach (var protectedDir in new[]
        {
            Path.Combine(appData, "Phrasor"), Path.Combine(appData, "PromptForge"),
            Path.Combine(localAppData, "Phrasor"), installDir,
        })
        {
            if (Overlaps(root, protectedDir))
                return Fail(real, "--data-dir cannot be, contain or sit inside Phrasor's real folders.");
        }

        var h = Hash(root);
        return new DataRoot
        {
            IsOverridden = true,
            DataDir = Path.Combine(root, "roaming", "Phrasor"),
            LegacyDataDir = Path.Combine(root, "roaming", "PromptForge"),
            HistoryDir = InstallPaths.HistoryDir(Path.Combine(root, "local")),
            MutexName = $"{MutexBase}.{h}",
            ShowSettingsEventName = $"{ShowSettingsBase}.{h}",
            ExitEventName = $"{ExitBase}.{h}",
            WebFolderName = $"{WebBase}-{h}",
        };
    }

    private static DataRoot Real(string appData, string localAppData) => new()
    {
        DataDir = Path.Combine(appData, "Phrasor"),
        LegacyDataDir = Path.Combine(appData, "PromptForge"),
        HistoryDir = InstallPaths.HistoryDir(localAppData),
        MutexName = MutexBase,
        ShowSettingsEventName = ShowSettingsBase,
        ExitEventName = ExitBase,
        WebFolderName = WebBase,
    };

    private static DataRoot Fail(DataRoot real, string message) => real with { Error = message };

    /// <summary>True when the two folders are the same, or one contains the other.</summary>
    private static bool Overlaps(string a, string b)
    {
        a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(a));
        b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(b));
        var sep = Path.DirectorySeparatorChar;
        return a.Equals(b, StringComparison.OrdinalIgnoreCase)
            || a.StartsWith(b + sep, StringComparison.OrdinalIgnoreCase)
            || b.StartsWith(a + sep, StringComparison.OrdinalIgnoreCase);
    }

    private static string Hash(string root) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToLowerInvariant())))[..HashHexLength].ToLowerInvariant();
}
