using System.Text.Json.Nodes;
using Phrasor.Core.Bridge;
using Phrasor.Core.Web;

namespace Phrasor.Core.Tests;

/// <summary>
/// The examples in <c>src/Phrasor.App/Web/contracts</c> are the written contract between the page and the app.
/// Each one is replayed through the real dispatcher, so a change on either side that is not mirrored in the file fails here.
/// </summary>
public class BridgeContractTests
{
    public static IEnumerable<object[]> Fixtures() =>
        Directory.GetFiles(ContractsDir(), "*.json").Order().Select(f => new object[] { Path.GetFileName(f) });

    [Fact]
    public void There_is_a_fixture_for_every_command()
    {
        var covered = Directory.GetFiles(ContractsDir(), "*.json").Select(f => JsonNode.Parse(File.ReadAllText(f))!["request"]?["type"]?.GetValue<string>());
        Assert.Empty(BridgeDispatcher.CommandNames.Except(covered.OfType<string>()));
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Fixture_request_gets_the_fixture_reply(string file)
    {
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(ContractsDir(), file)))!;
        var request = fixture["rawRequest"]?.GetValue<string>() ?? fixture["request"]!.ToJsonString();

        var dispatcher = new BridgeDispatcher(new BridgeDispatcherTests.FakeApi(), _ => { }, () => 0);
        var reply = dispatcher.Handle(WebSecurityPolicy.StartUrl, true, request);

        Assert.True(JsonNode.DeepEquals(fixture["reply"], JsonNode.Parse(reply!)), $"{file}: got {reply}");
    }

    internal static string ContractsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Phrasor.App", "Web", "contracts"))) dir = dir.Parent;
        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("repo root not found"), "src", "Phrasor.App", "Web", "contracts");
    }
}
