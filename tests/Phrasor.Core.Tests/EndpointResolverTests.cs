using Phrasor.Core.Settings;

namespace Phrasor.Core.Tests;

public class EndpointResolverTests
{
    [Fact]
    public void Same_model_on_primary_and_fallback_is_listed_once()
    {
        var secrets = new SettingsTests.MemorySecrets();
        secrets.Set("groq", "gsk_test");
        var settings = new AppSettings
        {
            Fallback = new EndpointSettings { Provider = "groq", Model = "openai/gpt-oss-120b" },
        };

        var endpoints = EndpointResolver.Resolve(settings, secrets);

        Assert.Single(endpoints);
    }

    [Fact]
    public void A_disabled_fallback_is_skipped()
    {
        var secrets = new SettingsTests.MemorySecrets();
        secrets.Set("groq", "gsk_test");
        var settings = new AppSettings();
        settings.Fallback.Enabled = false;

        var endpoints = EndpointResolver.Resolve(settings, secrets);

        Assert.Equal(["openai/gpt-oss-120b"], endpoints.Select(e => e.Model));
    }

    [Fact]
    public void A_fallback_with_a_blank_model_is_skipped()
    {
        var secrets = new SettingsTests.MemorySecrets();
        secrets.Set("groq", "gsk_test");
        var settings = new AppSettings();
        settings.Fallback.Model = "  ";

        var endpoints = EndpointResolver.Resolve(settings, secrets);

        Assert.Equal(["openai/gpt-oss-120b"], endpoints.Select(e => e.Model));
    }

    [Fact]
    public void A_fallback_missing_its_key_does_not_hide_a_working_primary()
    {
        var secrets = new SettingsTests.MemorySecrets();
        secrets.Set("groq", "gsk_test");
        var settings = new AppSettings
        {
            Fallback = new EndpointSettings { Provider = "gemini", Model = "gemini-test" },
        };

        var endpoints = EndpointResolver.Resolve(settings, secrets);

        Assert.Equal(["openai/gpt-oss-120b"], endpoints.Select(e => e.Model));
    }

    [Fact]
    public void Voice_endpoint_is_null_when_its_key_is_missing_and_built_when_saved()
    {
        var secrets = new SettingsTests.MemorySecrets();
        var settings = new AppSettings();

        Assert.Null(EndpointResolver.ResolveVoice(settings, secrets));

        secrets.Set(settings.Voice.Provider, "gsk_test");
        var voice = EndpointResolver.ResolveVoice(settings, secrets);

        Assert.NotNull(voice);
        Assert.Equal(settings.Voice.Model.Trim(), voice.Model);
    }
}
