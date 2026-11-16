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

    /// <summary>Removes everything stored.</summary>
    void Delete();
}

/// <summary>One encrypted file. Writes go to a temp file (already encrypted) and replace the real one, so a crash leaves the old file.</summary>
public sealed class EncryptedHistoryFile(string path, IBlobProtector protector) : IHistoryPersistence
{
    private string TempPath => path + ".tmp";
    private string BadPath => path + ".bad";

    public byte[]? Read() => File.Exists(path) ? protector.Unprotect(File.ReadAllBytes(path)) : null;

    public void Write(byte[] plain)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        try
        {
            File.WriteAllBytes(TempPath, protector.Protect(plain));
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
    }
}
