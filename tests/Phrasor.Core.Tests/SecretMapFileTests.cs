using Phrasor.Core.Settings;

namespace Phrasor.Core.Tests;

public sealed class SecretMapFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "secrets-" + Guid.NewGuid());
    private string File_ => Path.Combine(_dir, "secrets.dat");

    public SecretMapFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void Missing_file_gives_an_empty_map_and_no_bad_copy()
    {
        Assert.Empty(SecretMapFile.Read(File_));
        Assert.False(File.Exists(File_ + ".bad"));
    }

    [Fact]
    public void Good_file_is_read_and_not_copied_aside()
    {
        File.WriteAllText(File_, """{"groq":"AAAA","gemini":"BBBB"}""");

        var map = SecretMapFile.Read(File_);

        Assert.Equal(2, map.Count);
        Assert.False(File.Exists(File_ + ".bad"));
    }

    [Fact]
    public void Damaged_file_is_kept_as_bad_copy_and_logged_without_its_content()
    {
        const string damaged = """{"groq":"AAAA","gemini":"BB""";
        File.WriteAllText(File_, damaged);
        var logged = new List<string>();

        var map = SecretMapFile.Read(File_, logged.Add);

        Assert.Empty(map);
        Assert.Equal(damaged, File.ReadAllText(File_ + ".bad"));
        Assert.DoesNotContain(logged, l => l.Contains("AAAA"));
        Assert.Single(logged);
    }
}
