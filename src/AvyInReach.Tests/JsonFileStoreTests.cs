namespace AvyInReach.Tests;

public sealed class JsonFileStoreTests
{
    [Fact]
    public void GetMutexName_returns_same_name_for_same_root()
    {
        var paths = new AppPathsForTests();

        var first = JsonFileStore.GetMutexName(paths.RootDirectory);
        var second = JsonFileStore.GetMutexName(paths.RootDirectory);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task AcquireLockAsync_serializes_access_for_same_store_root()
    {
        var paths = new AppPathsForTests();

        var firstLease = await JsonFileStore.AcquireLockAsync(paths.RootDirectory, CancellationToken.None);

        using var secondCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var secondLeaseTask = Task.Run(
            () => JsonFileStore.AcquireLockAsync(paths.RootDirectory, secondCts.Token),
            secondCts.Token);

        await Task.Delay(100, CancellationToken.None);
        Assert.False(secondLeaseTask.IsCompleted);

        await firstLease.DisposeAsync();
        await using var secondLease = await secondLeaseTask.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
