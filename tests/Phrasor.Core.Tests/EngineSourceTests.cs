using Phrasor.Core.Web;

namespace Phrasor.Core.Tests;

public class EngineSourceTests
{
    [Fact]
    public void The_default_is_the_shared_runtime() => Assert.Null(EngineSource.Shared.BrowserFolder);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_setting_means_the_shared_runtime(string? value) => Assert.Same(EngineSource.Shared, EngineSource.FromSetting(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("engine")]
    [InlineData(@"..\engine")]
    [InlineData(@"\engine")]
    [InlineData(@"\\server\share\engine")]
    [InlineData("//server/share/engine")]
    [InlineData(@"\\?\UNC\server\share")]
    public void Folder_mode_refuses_empty_relative_and_network_paths(string? path)
    {
        Assert.Throws<ArgumentException>(() => EngineSource.Folder(path));
        if (!string.IsNullOrWhiteSpace(path)) Assert.Throws<ArgumentException>(() => EngineSource.FromSetting(path));
    }

    [Fact]
    public void A_full_local_path_is_accepted()
    {
        var path = Path.Combine(Path.GetTempPath(), "engine");
        Assert.Equal(path, EngineSource.Folder(path).BrowserFolder);
    }
}
