using LumaBrake.Persistence;

namespace LumaBrake.Persistence.Tests;

public sealed class HardwareBrightnessStateTests
{
    [Fact]
    public void WithWriteAttempt_TrimsTargetKeyAndReadsCaseInsensitively()
    {
        var attemptedAt = new DateTimeOffset(2026, 7, 26, 18, 0, 0, TimeSpan.Zero);

        var state = new HardwareBrightnessState()
            .WithWriteAttempt(@"  \\.\DISPLAY1#0  ", attemptedAt);

        Assert.Equal(attemptedAt, state.LastWriteAttemptAt(@"\\.\display1#0"));
        Assert.Equal([@"\\.\DISPLAY1#0"], state.LastWriteAttempts.Keys);
    }

    [Fact]
    public void WithWriteAttempt_ReplacesExistingTargetCaseInsensitively()
    {
        var first = new DateTimeOffset(2026, 7, 26, 18, 0, 0, TimeSpan.Zero);
        var second = first.AddMinutes(3);

        var state = new HardwareBrightnessState()
            .WithWriteAttempt(@"\\.\DISPLAY1#0", first)
            .WithWriteAttempt(@"\\.\display1#0", second);

        Assert.Equal(second, state.LastWriteAttemptAt(@"\\.\DISPLAY1#0"));
        Assert.Single(state.LastWriteAttempts);
    }

    [Fact]
    public void LastWriteAttemptAt_IgnoresBlankOrMissingTargetKey()
    {
        var state = new HardwareBrightnessState()
            .WithWriteAttempt(@"\\.\DISPLAY1#0", DateTimeOffset.UtcNow);

        Assert.Null(state.LastWriteAttemptAt(null));
        Assert.Null(state.LastWriteAttemptAt(" "));
        Assert.Null(state.LastWriteAttemptAt(@"\\.\DISPLAY2#0"));
    }

    [Fact]
    public void WithWriteAttempt_RejectsBlankTargetKey()
    {
        var state = new HardwareBrightnessState();

        Assert.Throws<ArgumentException>(() => state.WithWriteAttempt(" ", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Methods_TolerateNullDictionaryFromMalformedStateJson()
    {
        var attemptedAt = new DateTimeOffset(2026, 7, 26, 18, 0, 0, TimeSpan.Zero);
        var state = new HardwareBrightnessState { LastWriteAttempts = null! };

        Assert.Null(state.LastWriteAttemptAt(@"\\.\DISPLAY1#0"));

        var updated = state.WithWriteAttempt(@"\\.\DISPLAY1#0", attemptedAt);

        Assert.Equal(attemptedAt, updated.LastWriteAttemptAt(@"\\.\DISPLAY1#0"));
    }
}
