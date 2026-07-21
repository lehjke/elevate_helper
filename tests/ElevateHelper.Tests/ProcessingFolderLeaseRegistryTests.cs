using ElevateHelperWinUI.Services;

namespace ElevateHelper.Tests;

public sealed class ProcessingFolderLeaseRegistryTests
{
    [Fact]
    public void TryAcquire_BlocksOverlappingPathsOwnedByDifferentJobs()
    {
        ProcessingFolderLeaseRegistry registry = new();
        string root = Path.Combine(Path.GetTempPath(), "ElevateHelperLeaseTests", Guid.NewGuid().ToString("N"));
        string child = Path.Combine(root, "morning");

        Assert.True(registry.TryAcquire(root, "job-1", out IDisposable? rootLease));
        Assert.False(registry.TryAcquire(child, "job-2", out IDisposable? childLease));

        Assert.Null(childLease);
        rootLease!.Dispose();
        Assert.True(registry.TryAcquire(child, "job-2", out childLease));
        childLease!.Dispose();
    }

    [Fact]
    public void TryAcquire_AllowsScenarioLeaseOwnedBySameJob()
    {
        ProcessingFolderLeaseRegistry registry = new();
        string root = Path.Combine(Path.GetTempPath(), "ElevateHelperLeaseTests", Guid.NewGuid().ToString("N"));
        string child = Path.Combine(root, "morning");

        Assert.True(registry.TryAcquire(root, "job-1", out IDisposable? rootLease));
        Assert.True(registry.TryAcquire(child, "job-1", out IDisposable? childLease));

        childLease!.Dispose();
        rootLease!.Dispose();
    }
}
