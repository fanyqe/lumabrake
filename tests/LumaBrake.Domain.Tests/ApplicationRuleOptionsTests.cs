using LumaBrake.Domain.Rules;

namespace LumaBrake.Domain.Tests;

public sealed class ApplicationRuleOptionsTests
{
    [Fact]
    public void ExcludesAutomaticEffects_MatchesProcessNameWithOrWithoutExe()
    {
        var rules = new ApplicationRuleOptions
        {
            Entries =
            [
                new ApplicationRule
                {
                    Id = "browser",
                    ProcessName = "chrome.exe"
                }
            ]
        };

        Assert.True(rules.ExcludesAutomaticEffects(new ForegroundApplicationInfo("chrome", "Docs")));
    }

    [Fact]
    public void ExcludesAutomaticEffects_MatchesWindowTitleContains()
    {
        var rules = new ApplicationRuleOptions
        {
            Entries =
            [
                new ApplicationRule
                {
                    Id = "video",
                    WindowTitleContains = "Video Player"
                }
            ]
        };

        Assert.True(rules.ExcludesAutomaticEffects(new ForegroundApplicationInfo("player", "My Video Player")));
    }

    [Fact]
    public void ExcludesAutomaticEffects_RequiresEverySpecifiedCriterion()
    {
        var rules = new ApplicationRuleOptions
        {
            Entries =
            [
                new ApplicationRule
                {
                    Id = "game",
                    ProcessName = "game",
                    WindowTitleContains = "Settings"
                }
            ]
        };

        Assert.False(rules.ExcludesAutomaticEffects(new ForegroundApplicationInfo("game", "Main Menu")));
        Assert.True(rules.ExcludesAutomaticEffects(new ForegroundApplicationInfo("game.exe", "Game Settings")));
    }

    [Fact]
    public void ExcludesAutomaticEffects_IgnoresDisabledOrEmptyRules()
    {
        var rules = new ApplicationRuleOptions
        {
            Entries =
            [
                new ApplicationRule { Id = "disabled", ProcessName = "chrome", Enabled = false },
                new ApplicationRule { Id = "empty" }
            ]
        };

        Assert.False(rules.ExcludesAutomaticEffects(new ForegroundApplicationInfo("chrome", "Docs")));
    }

    [Fact]
    public void ExcludesNightColor_MatchesOnlyNightColorRules()
    {
        var rules = new ApplicationRuleOptions
        {
            Entries =
            [
                new ApplicationRule
                {
                    Id = "movie",
                    ProcessName = "vlc",
                    ExcludeAutomaticEffects = false,
                    ExcludeNightColor = true
                }
            ]
        };

        var application = new ForegroundApplicationInfo("vlc.exe", "Movie");

        Assert.False(rules.ExcludesAutomaticEffects(application));
        Assert.True(rules.ExcludesNightColor(application));
    }

    [Fact]
    public void ExcludesNightColor_UsesSharedApplicationMatchingRules()
    {
        var rules = new ApplicationRuleOptions
        {
            Entries =
            [
                new ApplicationRule
                {
                    Id = "browser",
                    ProcessName = "chrome",
                    WindowTitleContains = "Video",
                    ExcludeNightColor = true
                }
            ]
        };

        Assert.False(rules.ExcludesNightColor(new ForegroundApplicationInfo("chrome", "Docs")));
        Assert.True(rules.ExcludesNightColor(new ForegroundApplicationInfo("chrome.exe", "Video Player")));
    }

    [Fact]
    public void Clamp_NormalizesRulesAndKeepsLastDuplicateId()
    {
        var rules = new ApplicationRuleOptions
        {
            Entries =
            [
                new ApplicationRule { Id = "browser", ProcessName = "chrome" },
                new ApplicationRule { Id = "BROWSER", ProcessName = "edge" },
                new ApplicationRule { Id = "empty" }
            ]
        };

        var clamped = rules.Clamp();

        var rule = Assert.Single(clamped.Entries);
        Assert.Equal("BROWSER", rule.Id);
        Assert.Equal("edge", rule.ProcessName);
    }

    [Fact]
    public void Find_MatchesRuleIdsCaseInsensitively()
    {
        var rules = new ApplicationRuleOptions
        {
            Entries =
            [
                new ApplicationRule { Id = "Video", ProcessName = "vlc" }
            ]
        };

        Assert.Equal("Video", rules.Find(" video ")?.Id);
    }

    [Fact]
    public void Upsert_ReplacesExistingRuleAndIgnoresEmptyRules()
    {
        var rules = new ApplicationRuleOptions
        {
            Entries =
            [
                new ApplicationRule { Id = "video", ProcessName = "vlc" }
            ]
        };

        var updated = rules
            .Upsert(new ApplicationRule { Id = "VIDEO", ProcessName = "mpv" })
            .Upsert(new ApplicationRule { Id = "empty" });

        var rule = Assert.Single(updated.Entries);
        Assert.Equal("VIDEO", rule.Id);
        Assert.Equal("mpv", rule.ProcessName);
    }

    [Fact]
    public void Remove_DropsRuleByIdCaseInsensitively()
    {
        var rules = new ApplicationRuleOptions
        {
            Entries =
            [
                new ApplicationRule { Id = "video", ProcessName = "vlc" },
                new ApplicationRule { Id = "meeting", WindowTitleContains = "Meet" }
            ]
        };

        var updated = rules.Remove(" VIDEO ");

        var rule = Assert.Single(updated.Entries);
        Assert.Equal("meeting", rule.Id);
    }

    [Fact]
    public void SetEntryEnabled_UpdatesOnlyMatchingRule()
    {
        var rules = new ApplicationRuleOptions
        {
            Entries =
            [
                new ApplicationRule { Id = "video", ProcessName = "vlc" },
                new ApplicationRule { Id = "meeting", WindowTitleContains = "Meet" }
            ]
        };

        var updated = rules.SetEntryEnabled("VIDEO", enabled: false);

        Assert.False(updated.Find("video")?.Enabled);
        Assert.True(updated.Find("meeting")?.Enabled);
    }
}
