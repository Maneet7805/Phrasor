namespace Phrasor.Core.History;

/// <summary>Encrypts and decrypts bytes. The App supplies the Windows (DPAPI) one so Core stays free of Windows APIs.</summary>
public interface IBlobProtector
{
    byte[] Protect(byte[] plain);

    /// <summary>Throws (any exception) when the data is not ours, not for this user, or damaged.</summary>
    byte[] Unprotect(byte[] protectedData);
}

/// <summary>Where the store keeps its plain bytes. Implementations must never let plain text reach disk.</summary>
public interface IHistoryPersistence
{
    /// <summary>The stored bytes, or null when nothing is stored. Throws when they cannot be read or decrypted.</summary>
    byte[]? Read();

    void Write(byte[] plain);

    /// <summary>Keeps the unreadable data as a <c>.bad</c> copy and leaves nothing at the normal place.</summary>
    void MoveAside();

    /// <summary>Removes everything stored, including any <c>.bad</c> copy.</summary>
    void Delete();
}

/// <summary>The file was written by a newer Phrasor. It is an <see cref="IOException"/> so the store leaves it untouched and refuses to save.</summary>
public sealed class NewerHistoryFileException() : IOException("The History file was written by a newer version of Phrasor.");

/// <summary>
/// One file: a plain header (4-byte marker, 1 version byte) then the encrypted blob. Writes go to a temp file (already encrypted)
/// and replace the real one, so a crash leaves the old file. A file with no marker is the first format (no header) and reads as version 1.
/// </summary>
public sealed class EncryptedHistoryFile(string path, IBlobProtector protector) : IHistoryPersistence
{
    internal static readonly byte[] Marker = "PHST"u8.ToArray();
    internal const byte FormatVersion = 1;
    private const int HeaderLength = 5;

    private string TempPath => path + ".tmp";
    private string BadPath => path + ".bad";

    public byte[]? Read()
    {
        if (!File.Exists(path)) return null;
        var bytes = File.ReadAllBytes(path);
        if (!bytes.AsSpan().StartsWith(Marker)) return protector.Unprotect(bytes);
        if (bytes.Length < HeaderLength || bytes[Marker.Length] == 0) throw new InvalidDataException("Bad History header.");
        if (bytes[Marker.Length] > FormatVersion) throw new NewerHistoryFileException();
        return protector.Unprotect(bytes[HeaderLength..]);
    }

    public void Write(byte[] plain)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        try
        {
            File.WriteAllBytes(TempPath, [.. Marker, FormatVersion, .. protector.Protect(plain)]);
            File.Move(TempPath, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(TempPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    // shortcut: a second bad file replaces the first .bad; keep only the newest one.
    public void MoveAside() => File.Move(path, BadPath, overwrite: true);

    public void Delete()
    {
        File.Delete(path);
        File.Delete(TempPath);
        File.Delete(BadPath); // a set-aside copy is still the user's data
    }
}
