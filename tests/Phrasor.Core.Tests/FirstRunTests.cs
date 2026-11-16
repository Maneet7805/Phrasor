using System.Text.Json;
using Phrasor.Core.Bridge;
using Phrasor.Core.Settings;
using Phrasor.Core.Web;

namespace Phrasor.Core.Tests;

public class FirstRunTests
{
    private readonly BridgeDispatcherTests.FakeApi _api = new();
    private readonly List<string> _log = [];

    private string? Send(string type, string payload) =>
        new BridgeDispatcher(_api, _log.Add, () => 0).Handle(WebSecurityPolicy.StartUrl, true, $$"""{"id":"a1","type":"{{type}}","payload":{{payload}}}""");

    private static string? ErrorOf(string? reply) => JsonDocument.Parse(reply!).RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;

    [Fact]
    public void Apply_makes_the_provider_the_main_model_with_its_default_model_and_ends_first_run()
    {
        var settings = new AppSettings();
        FirstRun.Apply(settings, "gemini");
        Assert.Equal("gemini", settings.Primary.Provider);
        Assert.Equal(Llm.ProviderPresets.Gemini.DefaultModel, settings.Primary.Model);
        Assert.True(settings.FirstRunComplete);
    }

    [Fact]
    public void Test_and_save_pass_a_trimmed_key_for_a_listed_provider()
    {
        Assert.Null(ErrorOf(Send("firstrun.test", """{"provider":"groq","key":"  gsk_fake  "}""")));
        Assert.Null(ErrorOf(Send("firstrun.save", """{"provider":"gemini","key":"fake-key"}""")));
        Assert.Equal(new FirstRunKey("groq", "gsk_fake"), Assert.Single(_api.FirstRunTested));
        Assert.Equal(new FirstRunKey("gemini", "fake-key"), Assert.Single(_api.FirstRunSaved));
    }

    [Theory]
    [InlineData("""{"provider":"custom","key":"k"}""")]            // not on the short list
    [InlineData("""{"provider":"groq","key":""}""")]
    [InlineData("""{"provider":"groq","key":"   "}""")]
    [InlineData("""{"provider":"groq","key":"two\nlines"}""")]
    [InlineData("""{"provider":"groq","key":5}""")]
    [InlineData("""{"provider":"groq"}""")]
    [InlineData("""{"provider":"groq","key":"k","model":"x"}""")]  // nothing extra
    public void Bad_payloads_are_refused_and_reach_nothing(string payload)
    {
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Send("firstrun.test", payload)));
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Send("firstrun.save", payload)));
        Assert.Empty(_api.FirstRunTested);
        Assert.Empty(_api.FirstRunSaved);
    }

    [Fact]
    public void A_key_over_the_limit_is_refused()
    {
        var key = new string('k', SettingsPayload.MaxKeyChars + 1);
        Assert.Equal(BridgeError.BadRequest, ErrorOf(Send("firstrun.save", $$"""{"provider":"groq","key":"{{key}}"}""")));
    }

    [Fact]
    public void A_second_test_while_one_runs_is_busy()
    {
        _api.NextTest = null;
        Assert.Equal(BridgeError.Busy, ErrorOf(Send("firstrun.test", """{"provider":"groq","key":"k"}""")));
    }

    [Fact]
    public void The_log_line_holds_no_part_of_the_key()
    {
        Send("firstrun.save", """{"provider":"groq","key":"SECRETMARKER123"}""");
        Assert.All(_log, line => Assert.DoesNotContain("SECRETMARKER", line));
    }
}
