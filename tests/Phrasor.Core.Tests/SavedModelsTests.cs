using Phrasor.Core.Llm;
using Phrasor.Core.Optimization;
using Phrasor.Core.Settings;
using Xunit;

namespace Phrasor.Core.Tests;

public class SavedModelsTests
{
    private static AppSettings Old() => new()
    {
        Primary = new EndpointSettings { Provider = "groq", Model = "big" },
        Fallback = new EndpointSettings { Provider = "groq", Model = "small", Enabled = true },
    };

    private static string TempFile() => Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");

    [Fact]
    public void An_old_settings_file_gets_a_model_list_from_primary_and_fallback()
    {
        var path = TempFile();
        File.WriteAllText(path, """{"primary":{"provider":"gemini","model":"g1"},"fallback":{"provider":"groq","model":"small","enabled":true}}""");
        try
        {
            var s = SettingsStore.Load(path);
            Assert.Equal(2, s.SavedModels.Count);
            Assert.Equal("g1", s.SavedModels.Single(m => m.Id == s.DefaultModelId).Model);
            var backup = Assert.Single(s.BackupModelIds);
            Assert.Equal("small", s.SavedModels.Single(m => m.Id == backup).Model);
            Assert.All(s.SavedModels, m => Assert.False(m.SeparateKey));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_disabled_fallback_means_no_backups()
    {
        var s = Old();
        s.Fallback.Enabled = false;
        ModelList.Normalise(s);
        Assert.Empty(s.BackupModelIds);
        Assert.False(s.Fallback.Enabled);
    }

    [Fact]
    public void Changing_primary_later_changes_the_default_model_and_keeps_its_id_and_key_choice()
    {
        var s = Old();
        ModelList.Normalise(s);
        var id = s.DefaultModelId;
        s.SavedModels.Single(m => m.Id == id).SeparateKey = true;

        s.Primary = new EndpointSettings { Provider = "groq", Model = "other" }; // what an older window does
        ModelList.Normalise(s);

        Assert.Equal(id, s.DefaultModelId);
        Assert.Equal("other", s.SavedModels.Single(m => m.Id == id).Model);
        Assert.True(s.SavedModels.Single(m => m.Id == id).SeparateKey);
        Assert.Equal(ModelList.KeyIdFor(id), s.Primary.KeyId);
    }

    [Fact]
    public void Disabling_the_fallback_in_an_older_window_clears_the_chain()
    {
        var s = Old();
        ModelList.Normalise(s);
        s.Fallback = new EndpointSettings { Provider = "groq", Model = "small", Enabled = false };
        ModelList.Normalise(s);
        Assert.Empty(s.BackupModelIds);
    }

    [Fact]
    public void Settings_with_a_chain_survive_a_save_and_load()
    {
        var path = TempFile();
        try
        {
            var s = Old();
            ModelList.Normalise(s);
            s.SavedModels.Add(new SavedModel { Id = "third", Provider = "ollama", Model = "qwen3:4b" });
            s.BackupModelIds.Add("third");
            s.SavedModels[0].SeparateKey = true;
            SettingsStore.Save(path, s);

            var back = SettingsStore.Load(path);
            Assert.Equal(3, back.SavedModels.Count);
            Assert.Equal(2, back.BackupModelIds.Count);
            Assert.Equal("third", back.BackupModelIds[1]);
            Assert.True(back.SavedModels.Single(m => m.Id == back.DefaultModelId).SeparateKey);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Normalise_repairs_a_damaged_list()
    {
        var s = Old();
        s.SavedModels =
        [
            new SavedModel { Id = "a", Provider = "groq", Model = "big" },
            new SavedModel { Id = "a", Provider = "nonsense", Model = "dup" },
            new SavedModel { Id = "BAD ID", Provider = "groq", Model = "x" },
            new SavedModel { Id = "blank", Provider = "groq", Model = "  " },
        ];
        s.DefaultModelId = "a";
        s.BackupModelIds = ["a", "missing", "a"];

        ModelList.Normalise(s);

        Assert.Equal(s.SavedModels.Count, s.SavedModels.Select(m => m.Id).Distinct().Count());
        Assert.All(s.SavedModels, m => Assert.True(ModelList.IsValidId(m.Id)));
        Assert.DoesNotContain(s.SavedModels, m => m.Model.Trim().Length == 0);
        Assert.DoesNotContain("a", s.BackupModelIds);
        Assert.DoesNotContain("missing", s.BackupModelIds);
        Assert.Equal("groq", s.SavedModels.First(m => m.Model == "dup").Provider);
    }

    [Fact]
    public void The_list_never_grows_past_the_limit()
    {
        var s = Old();
        s.SavedModels = [.. Enumerable.Range(0, 15).Select(i => new SavedModel { Id = "m" + i, Model = "x" + i })];
        s.DefaultModelId = "m0";
        s.Primary = new EndpointSettings { Provider = "groq", Model = "x0" };
        ModelList.Normalise(s);
        Assert.True(s.SavedModels.Count <= ModelList.MaxSavedModels);
    }

    [Fact]
    public void Chain_puts_a_temporary_choice_first_then_default_then_backups()
    {
        var s = Old();
        s.SavedModels = [new() { Id = "a", Model = "big" }, new() { Id = "b", Model = "small" }, new() { Id = "c", Model = "third" }];
        s.DefaultModelId = "a";
        s.BackupModelIds = ["b"];

        Assert.Equal(["a", "b"], ModelList.Chain(s).Select(m => m.Id));
        Assert.Equal(["c", "a", "b"], ModelList.Chain(s, "c").Select(m => m.Id));
        Assert.Equal(["a", "b"], ModelList.Chain(s, "nope").Select(m => m.Id));
        Assert.Equal(["b", "a"], ModelList.Chain(s, "b").Select(m => m.Id));
    }

    [Fact]
    public void Resolve_gives_the_default_then_the_backups_and_a_temporary_choice_goes_first()
    {
        var s = Old();
        var secrets = new SettingsTests.MemorySecrets();
        secrets.Set("groq", "k");
        Assert.Equal(["big", "small"], EndpointResolver.Resolve(s, secrets).Select(e => e.Model));
        var id = s.SavedModels.First(m => m.Model == "small").Id;
        Assert.Equal(["small", "big"], EndpointResolver.Resolve(s, secrets, id).Select(e => e.Model));
        Assert.Equal("big", s.Primary.Model); // a temporary choice changes nothing that is saved
    }

    [Fact]
    public void A_model_with_its_own_key_uses_it_and_does_not_fall_back_to_the_provider_key()
    {
        var s = Old();
        ModelList.Normalise(s);
        var def = s.SavedModels.Single(m => m.Id == s.DefaultModelId);
        def.SeparateKey = true;
        var secrets = new SettingsTests.MemorySecrets();
        secrets.Set("groq", "provider-key");

        // The backup still uses the provider key; the default has no key of its own yet.
        Assert.Equal(["small"], EndpointResolver.Resolve(s, secrets).Select(e => e.Model));

        secrets.Set(ModelList.KeyIdFor(def.Id), "own-key");
        var endpoints = EndpointResolver.Resolve(s, secrets);
        Assert.Equal(["big", "small"], endpoints.Select(e => e.Model));
        Assert.Equal("own-key", endpoints[0].ApiKey);
        Assert.Equal("provider-key", endpoints[1].ApiKey);
    }

    [Fact]
    public void Resolve_names_the_missing_key_when_nothing_is_usable()
    {
        Assert.Throws<MissingKeyException>(() => EndpointResolver.Resolve(Old(), new SettingsTests.MemorySecrets()));
    }

    [Fact]
    public void Keys_of_removed_models_are_reported_so_they_can_be_forgotten()
    {
        var before = Old();
        ModelList.Normalise(before);
        before.SavedModels[0].SeparateKey = true;
        before.SavedModels[1].SeparateKey = true;
        var after = SettingsStore.Clone(before);
        after.SavedModels[0].SeparateKey = false;
        after.SavedModels.RemoveAt(1);

        Assert.Equal(
            [ModelList.KeyIdFor(before.SavedModels[0].Id), ModelList.KeyIdFor(before.SavedModels[1].Id)],
            ModelList.OrphanedKeyIds(before, after));
    }

    [Theory]
    [InlineData("ollama", null, true)]
    [InlineData("custom", "http://localhost:1234/v1", true)]
    [InlineData("custom", "http://127.0.0.1:8080/v1", true)]
    [InlineData("custom", "http://[::1]:8080/v1", true)]
    [InlineData("custom", "https://api.example.com/v1", false)]
    [InlineData("custom", "", false)]
    [InlineData("groq", "http://localhost:1/v1", false)]
    public void Local_means_this_PC_only(string provider, string? url, bool expected) =>
        Assert.Equal(expected, ModelList.IsLocal(provider, url));

    [Fact]
    public void Key_ids_for_models_are_recognised_and_provider_ids_are_not()
    {
        Assert.True(ModelList.IsKeyIdOfModel(ModelList.KeyIdFor("m1a2b3c4")));
        Assert.False(ModelList.IsKeyIdOfModel("groq"));
        Assert.False(ModelList.IsKeyIdOfModel("model:BAD ID"));
    }
}

public class ReviewWindowSettingsTests
{
    [Fact]
    public void Defaults_match_the_design()
    {
        var r = new AppSettings().ReviewWindow;
        Assert.Equal(6, r.BarCount);
        Assert.Equal("Auto", r.DefaultTask);
        Assert.False(r.HighlightChanges);
        Assert.False(r.RememberLastChoice);
        Assert.False(r.PrefetchOtherModes);
        Assert.Equal(["Coding", "Writing", "Research", "Image", "Video"], r.TaskOrder.Take(5));
        Assert.Equal(9, r.TaskOrder.Count);
    }

    [Fact]
    public void An_old_file_without_the_section_loads_with_defaults()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """{"defaultMode":"Light"}""");
            var s = SettingsStore.Load(path);
            Assert.Equal(6, s.ReviewWindow.BarCount);
            Assert.Equal(9, s.ReviewWindow.TaskOrder.Count);
            Assert.Equal(OptimizeMode.Light, s.DefaultMode);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Normalise_fixes_order_count_and_names()
    {
        var r = new ReviewWindowSettings
        {
            TaskOrder = ["video", "Video", "Coding", "Auto", "nonsense", " writing "],
            BarCount = 99, DefaultTask = "bogus", LastTask = "image", LastMode = "huge",
        };
        r.Normalise();
        Assert.Equal(["Video", "Coding", "Writing"], r.TaskOrder.Take(3));
        Assert.Equal(9, r.TaskOrder.Count);
        Assert.Equal(r.TaskOrder.Count, r.TaskOrder.Distinct().Count());
        Assert.DoesNotContain("Auto", r.TaskOrder);
        Assert.Equal(10, r.BarCount);
        Assert.Equal("Auto", r.DefaultTask);
        Assert.Equal("Image", r.LastTask);
        Assert.Equal("Standard", r.LastMode);
        r.BarCount = 1;
        r.Normalise();
        Assert.Equal(4, r.BarCount);
    }

    [Fact]
    public void A_null_list_in_the_file_is_repaired()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """{"reviewWindow":{"taskOrder":null,"barCount":3}}""");
            var s = SettingsStore.Load(path);
            Assert.Equal(9, s.ReviewWindow.TaskOrder.Count);
            Assert.Equal(4, s.ReviewWindow.BarCount);
        }
        finally { File.Delete(path); }
    }
}
