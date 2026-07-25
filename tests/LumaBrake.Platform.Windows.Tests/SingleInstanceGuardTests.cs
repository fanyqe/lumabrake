using LumaBrake.Platform.Windows;

namespace LumaBrake.Platform.Windows.Tests;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void Acquire_AllowsOnlyOneOwnerForMutexName()
    {
        var mutexName = CreateMutexName();

        using var first = SingleInstanceGuard.Acquire(mutexName);
        using var second = SingleInstanceGuard.Acquire(mutexName);

        Assert.True(first.HasHandle);
        Assert.False(second.HasHandle);
    }

    [Fact]
    public void Dispose_ReleasesMutexForNextOwner()
    {
        var mutexName = CreateMutexName();

        using (var first = SingleInstanceGuard.Acquire(mutexName))
        {
            Assert.True(first.HasHandle);
        }

        using var second = SingleInstanceGuard.Acquire(mutexName);
        Assert.True(second.HasHandle);
    }

    [Fact]
    public void Acquire_RejectsBlankMutexName()
    {
        Assert.Throws<ArgumentException>(() => SingleInstanceGuard.Acquire(" "));
    }

    private static string CreateMutexName() =>
        $@"Local\LumaBrake.Tests.{Guid.NewGuid():N}";
}
