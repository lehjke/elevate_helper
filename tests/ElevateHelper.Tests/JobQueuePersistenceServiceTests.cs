using ElevateHelperWinUI.Models;
using ElevateHelperWinUI.Services;

namespace ElevateHelper.Tests;

public sealed class JobQueuePersistenceServiceTests
{
    [Fact]
    public void LoadInterruptedJobs_DoesNotConsumeQueueBeforeAcknowledgement()
    {
        using PersistenceWorkspace workspace = new();
        string queuePath = Path.Combine(workspace.RootPath, "active-jobs.json");
        JobQueuePersistenceService service = new(queuePath);
        PersistedJobSnapshot snapshot = CreateSnapshot(workspace.RootPath);

        service.SaveActiveJobs([snapshot]);

        Assert.Single(service.LoadInterruptedJobs());
        Assert.Single(service.LoadInterruptedJobs());

        service.ClearInterruptedJobs();

        Assert.Empty(service.LoadInterruptedJobs());
    }

    [Fact]
    public void SaveActiveJobs_RetriesSameStateAfterDirectoryCreationFailure()
    {
        using PersistenceWorkspace workspace = new();
        string blockedDirectory = Path.Combine(workspace.RootPath, "blocked");
        string queuePath = Path.Combine(blockedDirectory, "active-jobs.json");
        File.WriteAllText(blockedDirectory, "not a directory");
        JobQueuePersistenceService service = new(queuePath);
        PersistedJobSnapshot snapshot = CreateSnapshot(workspace.RootPath);

        service.SaveActiveJobs([snapshot]);
        Assert.False(File.Exists(queuePath));

        File.Delete(blockedDirectory);
        Directory.CreateDirectory(blockedDirectory);
        service.SaveActiveJobs([snapshot]);

        Assert.True(File.Exists(queuePath));
        Assert.Single(service.LoadInterruptedJobs());
    }

    private static PersistedJobSnapshot CreateSnapshot(string path)
    {
        return new PersistedJobSnapshot(
            path,
            BuildingType.Office,
            IncludeLunchPeak: true,
            Title: "Office / G1",
            ReportOutputRoot: path,
            DateTimeOffset.UtcNow);
    }

    private sealed class PersistenceWorkspace : IDisposable
    {
        public PersistenceWorkspace()
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                "ElevateHelperQueueTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(RootPath, recursive: true);
            }
            catch
            {
            }
        }
    }
}
