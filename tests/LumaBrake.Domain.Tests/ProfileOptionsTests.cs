using LumaBrake.Domain.Analysis;
using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Profiles;

namespace LumaBrake.Domain.Tests;

public sealed class ProfileOptionsTests
{
    [Fact]
    public void ProfileClamp_NormalizesValues()
    {
        var profile = new LumaBrakeProfile
        {
            Id = "  work  ",
            Name = "  Work  ",
            UserMaximumOpacity = 2,
            ManualDimmingOpacity = -1,
            Tint = new TintOptions { Enabled = true, Red = 255, Green = 214, Blue = 244, Opacity = 3 },
            FocusDimming = new FocusDimmingOptions { Enabled = true, Opacity = 2 }
        }.Clamp();

        Assert.Equal("work", profile.Id);
        Assert.Equal("Work", profile.Name);
        Assert.Equal(0.92, profile.UserMaximumOpacity, precision: 12);
        Assert.Equal(0, profile.ManualDimmingOpacity, precision: 12);
        Assert.True(profile.Tint.Enabled);
        Assert.Equal(255, profile.Tint.Red);
        Assert.Equal(214, profile.Tint.Green);
        Assert.Equal(0.60, profile.Tint.Opacity, precision: 12);
        Assert.True(profile.FocusDimming.Enabled);
        Assert.Equal(0.60, profile.FocusDimming.Opacity, precision: 12);
    }

    [Fact]
    public void Upsert_ReplacesExistingProfileCaseInsensitively()
    {
        var options = new ProfileOptions()
            .Upsert(new LumaBrakeProfile { Id = "work", Name = "Old", ManualDimmingOpacity = 0.10 })
            .Upsert(new LumaBrakeProfile { Id = "WORK", Name = "New", ManualDimmingOpacity = 0.20 });

        var profile = Assert.Single(options.Clamp().Entries);
        Assert.Equal("WORK", profile.Id);
        Assert.Equal("New", profile.Name);
        Assert.Equal(0.20, profile.ManualDimmingOpacity, precision: 12);
        Assert.Equal("New", options.Find("work")?.Name);
    }

    [Fact]
    public void Remove_DropsMatchingProfileOnly()
    {
        var options = new ProfileOptions
        {
            Entries =
            [
                new LumaBrakeProfile { Id = "work" },
                new LumaBrakeProfile { Id = "night" }
            ]
        }.Remove("WORK");

        var profile = Assert.Single(options.Entries);
        Assert.Equal("night", profile.Id);
    }

    [Fact]
    public void FindAndRemove_IgnoreBlankIds()
    {
        var options = new ProfileOptions
        {
            Entries = [new LumaBrakeProfile { Id = "night" }]
        };

        Assert.Null(options.Find(" "));
        Assert.Single(options.Remove(" ").Entries);
    }
}
