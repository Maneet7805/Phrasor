using Phrasor.Core.Install;

namespace Phrasor.Core.Tests;

public class DataRootTests
{
    private static readonly string Base = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "pf-dataroot-tests"));
    private static readonly string Roaming = Path.Combine(Base, "Roaming");
    private static readonly string Local = Path.Combine(Base, "Local");
    private static readonly string Install = InstallPaths.InstallDir(Local);
    private static readonly string Scratch = Path.Combine(Base, "scratch");

    private static DataRoot Resolve(params string[] args) => DataRoot.Resolve(args, Roaming, Local, Install);

    [Fact]
    public void Without_the_switch_the_paths_and_names_are_exactly_todays()
    {
        var r = Resolve("--background");
        Assert.True(r.Ok);
        Assert.False(r.IsOverridden);
        Assert.Equal(Path.Combine(Roaming, "Phrasor"), r.DataDir);
        Assert.Equal(Path.Combine(Roaming, "PromptForge"), r.LegacyDataDir);
        Assert.Equal(InstallPaths.HistoryDir(Local), r.HistoryDir);
        Assert.Equal(@"Local\Phrasor.SingleInstance", r.MutexName);
        Assert.Equal(@"Local\Phrasor.ShowSettings", r.ShowSettingsEventName);
        Assert.Equal(@"Local\Phrasor.Exit", r.ExitEventName);
        Assert.Equal("Phrasor-web", r.WebFolderName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_switch_gives_the_three_folders_under_the_root(bool joined)
    {
        var r = joined ? Resolve("--data-dir=" + Scratch) : Resolve("--data-dir", Scratch);
        Assert.True(r.Ok);
        Assert.True(r.IsOverridden);
        Assert.Equal(Path.Combine(Scratch, "roaming", "Phrasor"), r.DataDir);
        Assert.Equal(Path.Combine(Scratch, "roaming", "PromptForge"), r.LegacyDataDir);
        Assert.Equal(InstallPaths.HistoryDir(Path.Combine(Scratch, "local")), r.HistoryDir);
        Assert.StartsWith(@"Local\Phrasor.SingleInstance.", r.MutexName);
        Assert.StartsWith(@"Local\Phrasor.ShowSettings.", r.ShowSettingsEventName);
        Assert.StartsWith(@"Local\Phrasor.Exit.", r.ExitEventName);
        Assert.StartsWith("Phrasor-web-", r.WebFolderName);
    }

    [Theory]
    [InlineData(@"D:\scratch""")] // PowerShell 5.1 turns "D:\scratch\" into this
    [InlineData(@"D:\scr<atch")]
    [InlineData(@"D:\scr>atch")]
    [InlineData(@"D:\scr|atch")]
    [InlineData(@"D:\scr?atch")]
    [InlineData(@"D:\scr*atch")]
    [InlineData(@"D:\CON")]
    [InlineData(@"D:\scratch\nul")]
    [InlineData(@"D:\scratch\com1.txt")]
    [InlineData(@"D:\LPT9")]
    public void A_value_that_cannot_be_a_folder_is_refused(string value)
    {
        var r = Resolve("--data-dir", value);
        Assert.False(r.Ok);
        Assert.False(string.IsNullOrWhiteSpace(r.Error));
    }

    [Theory]
    [InlineData("--data-dir")]
    [InlineData("--data-dir|")]
    [InlineData("--data-dir|   ")]
    [InlineData("--data-dir=")]
    [InlineData("--data-dir|--portable")]
    [InlineData("--data-dir|relative\\folder")]
    [InlineData("--data-dir=relative")]
    public void A_missing_empty_or_relative_value_is_refused_never_ignored(string joined)
    {
        var r = Resolve(joined.Length == 0 ? [] : joined.Split('|'));
        Assert.False(r.Ok);
        Assert.False(string.IsNullOrWhiteSpace(r.Error));
    }

    [Fact]
    public void Giving_the_switch_twice_is_refused()
    {
        Assert.False(Resolve("--data-dir", Scratch, "--data-dir=" + Scratch).Ok);
    }

    [Fact]
    public void Network_and_device_paths_are_refused()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.False(Resolve("--data-dir", @"\\server\share\x").Ok);
        Assert.False(Resolve("--data-dir", @"\\?\C:\scratch").Ok);
        Assert.False(Resolve("--data-dir", @"\\.\C:\scratch").Ok);
    }

    [Fact]
    public void A_drive_root_is_refused_even_when_dots_lead_there()
    {
        var root = Path.GetPathRoot(Scratch)!;
        Assert.False(Resolve("--data-dir", root).Ok);
        Assert.False(Resolve("--data-dir", Path.Combine(Scratch, "..", "..", "..", "..", "..", "..", "..", "..", "..")).Ok);
    }

    [Fact]
    public void Real_data_folders_and_their_parents_and_children_are_refused()
    {
        var real = new[]
        {
            Path.Combine(Roaming, "Phrasor"), Path.Combine(Roaming, "PromptForge"),
            Path.Combine(Local, "Phrasor"), Install,
        };
        foreach (var folder in real)
        {
            Assert.False(Resolve("--data-dir", folder).Ok);
            Assert.False(Resolve("--data-dir", Path.Combine(folder, "sub")).Ok);
            Assert.False(Resolve("--data-dir", Path.GetDirectoryName(folder)!).Ok);
        }
        Assert.False(Resolve("--data-dir", Roaming).Ok);
        Assert.False(Resolve("--data-dir", Local).Ok);
        Assert.False(Resolve("--data-dir", Path.Combine(Roaming, "Phrasor", "..", "Phrasor")).Ok);
    }

    [Fact]
    public void A_folder_with_a_similar_name_is_not_mistaken_for_real_data()
    {
        Assert.True(Resolve("--data-dir", Path.Combine(Roaming, "Phrasor-test")).Ok);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("a\\b\\c")]
    [InlineData("a\\..\\b")]
    public void No_delete_target_is_the_root_or_a_parent_of_it(string relative)
    {
        var r = Resolve("--data-dir", Path.Combine(Scratch, relative));
        Assert.True(r.Ok);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(Scratch, relative)));
        foreach (var target in new[] { r.DataDir, r.LegacyDataDir, r.HistoryDir })
        {
            var t = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
            Assert.StartsWith(root + Path.DirectorySeparatorChar, t, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_same_root_written_differently_gives_the_same_names()
    {
        var a = Resolve("--data-dir", Scratch);
        var b = Resolve("--data-dir", Scratch.ToUpperInvariant() + Path.DirectorySeparatorChar);
        var c = Resolve("--data-dir", Path.Combine(Scratch, "x", ".."));
        if (OperatingSystem.IsWindows()) Assert.Equal(a.MutexName, b.MutexName);
        Assert.Equal(a.MutexName, c.MutexName);
        Assert.Equal(a.WebFolderName, c.WebFolderName);
    }

    [Fact]
    public void Different_roots_give_different_names_and_all_names_stay_short()
    {
        var a = Resolve("--data-dir", Path.Combine(Scratch, "one"));
        var b = Resolve("--data-dir", Path.Combine(Scratch, "two"));
        Assert.NotEqual(a.MutexName, b.MutexName);
        Assert.NotEqual(a.ExitEventName, b.ExitEventName);
        Assert.NotEqual(a.WebFolderName, b.WebFolderName);
        foreach (var n in new[] { a.MutexName, a.ShowSettingsEventName, a.ExitEventName, a.WebFolderName })
            Assert.True(n.Length < 200);
    }

    [Fact]
    public void The_switch_after_a_mode_switch_is_not_an_override()
    {
        foreach (var mode in new[] { "--eval", "--eval-compare", "--rejudge" })
        {
            var r = Resolve(mode, "--data-dir", Scratch);
            Assert.True(r.Ok);
            Assert.False(r.IsOverridden);
        }
    }

    [Fact]
    public void A_value_that_looks_like_the_switch_is_taken_as_its_value_not_scanned_again()
    {
        var r = Resolve("--data-dir", Scratch, "--note", "--data-dirx");
        Assert.True(r.Ok);
        Assert.True(r.IsOverridden);
    }
}
