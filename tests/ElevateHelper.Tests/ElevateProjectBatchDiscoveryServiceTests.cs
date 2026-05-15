using ElevateHelperWinUI.Models;
using ElevateHelperWinUI.Services;

namespace ElevateHelper.Tests;

public sealed class ElevateProjectBatchDiscoveryServiceTests
{
    [Fact]
    public void Discover_FindsOneProjectPerGroupFolder()
    {
        using BatchDiscoveryWorkspace workspace = new();
        string office = workspace.CreateElvx(Path.Combine("Office", "G1"), "office.elvx");
        string residence = workspace.CreateElvx(Path.Combine("Res", "G2"), "res.elvx");
        string hotel = workspace.CreateElvx(Path.Combine("Hotel", "G3"), "hotel.elvx");

        ProjectBatchDiscoveryResult result = new ElevateProjectBatchDiscoveryService().Discover(workspace.RootPath);

        Assert.Empty(result.Warnings);
        Assert.Empty(result.UnknownElvxFiles);
        Assert.Collection(
            result.Jobs.OrderBy(job => job.ElvxPath, StringComparer.OrdinalIgnoreCase),
            job =>
            {
                Assert.Equal(BuildingType.Hotel, job.BuildingType);
                Assert.Equal("G3", job.GroupName);
                Assert.Equal(hotel, job.ElvxPath);
            },
            job =>
            {
                Assert.Equal(BuildingType.Office, job.BuildingType);
                Assert.Equal("G1", job.GroupName);
                Assert.Equal(office, job.ElvxPath);
            },
            job =>
            {
                Assert.Equal(BuildingType.Residence, job.BuildingType);
                Assert.Equal("G2", job.GroupName);
                Assert.Equal(residence, job.ElvxPath);
            });
    }

    [Fact]
    public void Discover_SkipsGroupsWithMultipleSourceFiles()
    {
        using BatchDiscoveryWorkspace workspace = new();
        _ = workspace.CreateElvx(Path.Combine("Office", "G1"), "a.elvx");
        _ = workspace.CreateElvx(Path.Combine("Office", "G1"), "b.elvx");

        ProjectBatchDiscoveryResult result = new ElevateProjectBatchDiscoveryService().Discover(workspace.RootPath);

        Assert.Empty(result.Jobs);
        Assert.Contains(result.Warnings, warning => warning.Path.EndsWith(Path.Combine("Office", "G1"), StringComparison.Ordinal));
    }

    [Fact]
    public void Discover_IgnoresTrackedGeneratedCopies()
    {
        using BatchDiscoveryWorkspace workspace = new();
        string source = workspace.CreateElvx(Path.Combine("Res", "G1"), "Project01.elvx");
        _ = workspace.CreateElvx(Path.Combine("Res", "G1"), "Project02.elvx");
        workspace.WriteFile(Path.Combine("Res", "G1", ".elevate-helper.generated-copies.txt"), "Project02.elvx");

        ProjectBatchDiscoveryResult result = new ElevateProjectBatchDiscoveryService().Discover(workspace.RootPath);

        ProjectBatchJob job = Assert.Single(result.Jobs);
        Assert.Equal(source, job.ElvxPath);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Discover_ReturnsUnknownFilesOutsideKnownBuildingFolders()
    {
        using BatchDiscoveryWorkspace workspace = new();
        string unknown = workspace.CreateElvx(Path.Combine("Other", "G1"), "unknown.elvx");
        _ = workspace.CreateElvx(Path.Combine("Office", "G1"), "office.elvx");

        ProjectBatchDiscoveryResult result = new ElevateProjectBatchDiscoveryService().Discover(workspace.RootPath);

        Assert.Single(result.Jobs);
        Assert.Equal([unknown], result.UnknownElvxFiles);
    }

    private sealed class BatchDiscoveryWorkspace : IDisposable
    {
        public string RootPath { get; } = Path.Combine(
            Path.GetTempPath(),
            "ElevateHelperBatchDiscoveryTests",
            Guid.NewGuid().ToString("N"));

        public BatchDiscoveryWorkspace()
        {
            Directory.CreateDirectory(RootPath);
        }

        public string CreateElvx(string relativeFolder, string fileName)
        {
            string folder = Path.Combine(RootPath, relativeFolder);
            Directory.CreateDirectory(folder);
            string filePath = Path.Combine(folder, fileName);
            File.WriteAllText(filePath, "<ElevateDocument />");
            return filePath;
        }

        public void WriteFile(string relativePath, string content)
        {
            string filePath = Path.Combine(RootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, content);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath))
                {
                    Directory.Delete(RootPath, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}
