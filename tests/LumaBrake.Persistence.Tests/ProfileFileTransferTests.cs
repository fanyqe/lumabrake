using LumaBrake.Domain.Effects;
using LumaBrake.Domain.Profiles;
using LumaBrake.Persistence;

namespace LumaBrake.Persistence.Tests;

public sealed class ProfileFileTransferTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"lumabrake-profile-transfer-{Guid.NewGuid():N}");

    [Fact]
    public void Export_CreatesDirectoryAndWritesClampedProfileJson()
    {
        var outputPath = Path.Combine(_root, "profiles", "night.json");

        var exportedPath = ProfileFileTransfer.Export(
            new LumaBrakeProfile
            {
                Id = "  night  ",
                Name = "  Night  ",
                ManualDimmingOpacity = 2,
                Tint = new TintOptions { Enabled = true, Red = 255, Green = 214, Blue = 170, Opacity = 2 }
            },
            outputPath);

        var json = File.ReadAllText(exportedPath);
        Assert.Equal(Path.GetFullPath(outputPath), exportedPath);
        Assert.Contains("\"id\": \"night\"", json, StringComparison.Ordinal);
        Assert.Contains("\"manualDimmingOpacity\": 0.9", json, StringComparison.Ordinal);
        Assert.Contains("\"opacity\": 0.6", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_ReadsCaseInsensitiveIdAndClampsProfile()
    {
        Directory.CreateDirectory(_root);
        var inputPath = Path.Combine(_root, "profile.json");
        File.WriteAllText(
            inputPath,
            """
            {
              "ID": "  imported  ",
              "name": "  Imported  ",
              "userMaximumOpacity": 2,
              "manualDimmingOpacity": 2,
              "focusDimming": {
                "enabled": true,
                "opacity": 2
              }
            }
            """);

        var profile = ProfileFileTransfer.Import(inputPath);

        Assert.Equal("imported", profile.Id);
        Assert.Equal("Imported", profile.Name);
        Assert.Equal(0.92, profile.UserMaximumOpacity, precision: 12);
        Assert.Equal(0.90, profile.ManualDimmingOpacity, precision: 12);
        Assert.True(profile.FocusDimming.Enabled);
        Assert.Equal(0.60, profile.FocusDimming.Opacity, precision: 12);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{ \"id\": \"   \" }")]
    [InlineData("[]")]
    public void Import_RejectsUnsafeProfileShapes(string json)
    {
        Directory.CreateDirectory(_root);
        var inputPath = Path.Combine(_root, "profile.json");
        File.WriteAllText(inputPath, json);

        Assert.Throws<InvalidDataException>(() => ProfileFileTransfer.Import(inputPath));
    }

    [Fact]
    public void Import_RejectsInvalidJson()
    {
        Directory.CreateDirectory(_root);
        var inputPath = Path.Combine(_root, "profile.json");
        File.WriteAllText(inputPath, "{");

        Assert.Throws<InvalidDataException>(() => ProfileFileTransfer.Import(inputPath));
    }

    [Fact]
    public void Import_RejectsMissingFile()
    {
        var inputPath = Path.Combine(_root, "missing.json");

        Assert.Throws<FileNotFoundException>(() => ProfileFileTransfer.Import(inputPath));
    }

    [Fact]
    public void Import_RejectsOversizedFile()
    {
        Directory.CreateDirectory(_root);
        var inputPath = Path.Combine(_root, "large.json");
        File.WriteAllBytes(inputPath, new byte[ProfileFileTransfer.MaximumProfileFileBytes + 1]);

        Assert.Throws<InvalidDataException>(() => ProfileFileTransfer.Import(inputPath));
    }

    [Fact]
    public void Export_RemovesTemporaryFileWhenFinalMoveFails()
    {
        Directory.CreateDirectory(_root);
        var directoryAsOutput = Path.Combine(_root, "profile-output");
        Directory.CreateDirectory(directoryAsOutput);

        var exception = Record.Exception(() => ProfileFileTransfer.Export(new LumaBrakeProfile { Id = "night" }, directoryAsOutput));

        Assert.True(exception is IOException or UnauthorizedAccessException, exception?.GetType().FullName);
        Assert.Empty(Directory.EnumerateFiles(_root, "profile-output.*.tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
