using System.Text.Json;
using System.Xml.Linq;
using ElevateHelperWinUI.Models;
using ElevateHelperWinUI.Services;

namespace ElevateHelper.Tests;

public sealed class ElevateProcessingServiceTests
{
    [Theory]
    [InlineData(BuildingType.Office, 13)]
    [InlineData(BuildingType.Residence, 8)]
    [InlineData(BuildingType.Hotel, 13)]
    public void GetDefaultCopies_ReturnsExpectedValue(BuildingType buildingType, int expected)
    {
        ElevateProcessingService service = new(new FakeLauncherService());

        int actual = service.GetDefaultCopies(buildingType);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ModifyHandlingCapacity_UpdatesXmlValues()
    {
        using TestWorkspace workspace = new();
        string elvxPath = workspace.CreateSampleElvx("Project01.elvx");
        ElevateProcessingService service = new(new FakeLauncherService());

        service.ModifyHandlingCapacity(elvxPath, 7);

        XDocument xml = XDocument.Load(elvxPath);
        string? handlingCapacity = xml
            .Descendants("PassengerData")
            .Elements("Standard")
            .Elements("HandlingCapacity")
            .FirstOrDefault()
            ?.Value;
        IEnumerable<XElement> periods = xml
            .Descendants("PassengerData")
            .Elements("Traffic")
            .Elements("Period")
            .Where(period => string.Equals((string?)period.Attribute("Id"), "0", StringComparison.Ordinal));

        Assert.Equal("7", handlingCapacity);
        Assert.All(periods, period => Assert.Equal("7", (string?)period.Attribute("TotalArrivalRate")));
    }

    [Theory]
    [InlineData("Morning", "100", "0", "0")]
    [InlineData("Lunch", "45", "45", "10")]
    public void ModifyBuildingTypeOffice_SetsExpectedTrafficSplits(
        string peak,
        string expectedUp,
        string expectedDown,
        string expectedInterfloor)
    {
        using TestWorkspace workspace = new();
        string elvxPath = workspace.CreateSampleElvx("Project01.elvx");
        ElevateProcessingService service = new(new FakeLauncherService());

        service.ModifyBuildingTypeOffice(elvxPath, peak);

        XDocument xml = XDocument.Load(elvxPath);
        XElement period = xml
            .Descendants("PassengerData")
            .Elements("Traffic")
            .Elements("Period")
            .First(period => string.Equals((string?)period.Attribute("Id"), "0", StringComparison.Ordinal));
        XElement buildingData = xml.Descendants("BuildingData").First();

        Assert.Equal(expectedUp, (string?)period.Attribute("SplitUp"));
        Assert.Equal(expectedDown, (string?)period.Attribute("SplitDown"));
        Assert.Equal(expectedInterfloor, (string?)period.Attribute("SplitInterfloor"));
        Assert.Equal("1", (string?)buildingData.Attribute("BuildingType"));
    }

    [Theory]
    [InlineData(BuildingType.Residence, "3")]
    [InlineData(BuildingType.Hotel, "2")]
    public void ModifyBuildingTypeResidence_SetsExpectedBuildingType(
        BuildingType buildingType,
        string expectedType)
    {
        using TestWorkspace workspace = new();
        string elvxPath = workspace.CreateSampleElvx("Project01.elvx");
        ElevateProcessingService service = new(new FakeLauncherService());

        service.ModifyBuildingTypeResidence(elvxPath, buildingType);

        XDocument xml = XDocument.Load(elvxPath);
        XElement period = xml
            .Descendants("PassengerData")
            .Elements("Traffic")
            .Elements("Period")
            .First(period => string.Equals((string?)period.Attribute("Id"), "0", StringComparison.Ordinal));
        XElement buildingData = xml.Descendants("BuildingData").First();

        Assert.Equal("50", (string?)period.Attribute("SplitUp"));
        Assert.Equal("50", (string?)period.Attribute("SplitDown"));
        Assert.Equal("0", (string?)period.Attribute("SplitInterfloor"));
        Assert.Equal(expectedType, (string?)buildingData.Attribute("BuildingType"));
    }

    [Fact]
    public void ModifyTitle_AppendsPeakSuffix()
    {
        using TestWorkspace workspace = new();
        string elvxPath = workspace.CreateSampleElvx("Project01.elvx");
        ElevateProcessingService service = new(new FakeLauncherService());

        service.ModifyTitle(elvxPath, "Morning");

        XDocument xml = XDocument.Load(elvxPath);
        XElement jobData = xml.Descendants("JobData").First();

        Assert.Equal("Test Project (утренний пик)", (string?)jobData.Attribute("JobTitle"));
    }

    [Fact]
    public void GetArea_WritesFloorAreaCsv()
    {
        using TestWorkspace workspace = new();
        _ = workspace.CreateSampleElvx("Project01.elvx");
        ElevateProcessingService service = new(new FakeLauncherService());

        service.GetArea(workspace.Path);

        string csvPath = System.IO.Path.Combine(workspace.Path, "floor_area.csv");
        Assert.True(File.Exists(csvPath));

        string[] lines = File.ReadAllLines(csvPath);
        Assert.Equal("CarId;FloorAreaM2", lines[0]);
        Assert.Contains("1;10.5", lines);
        Assert.Contains("2;12", lines);
    }

    [Fact]
    public async Task RunAsync_OfficeFlow_UsesLauncherAndGeneratesMorningCsv()
    {
        using TestWorkspace workspace = new();
        _ = workspace.CreateSampleElvx("Project01.elvx");
        FakeLauncherService launcher = new();
        ElevateProcessingService service = new(launcher);

        ProcessingResult result = await service.RunAsync(
            copiesCount: 2,
            path: workspace.Path,
            buildingType: BuildingType.Office,
            includeLunchPeak: false);

        Assert.True(result.Success, result.Message);
        Assert.Single(launcher.OfficeCalls);
        Assert.Equal((workspace.Path, false), launcher.OfficeCalls[0]);
        Assert.True(File.Exists(System.IO.Path.Combine(workspace.Path, "morning", "floor_area.csv")));
        Assert.False(Directory.Exists(System.IO.Path.Combine(workspace.Path, "lunch")));
    }

    [Fact]
    public async Task RunAsync_OfficeFlow_PreservesCompletedMorningWhenAddingLunch()
    {
        using TestWorkspace workspace = new();
        _ = workspace.CreateSampleElvx("Project01.elvx");
        string morningPath = System.IO.Path.Combine(workspace.Path, "morning");
        Directory.CreateDirectory(morningPath);
        File.WriteAllText(System.IO.Path.Combine(morningPath, "Project01.elvx"), "<Project />");
        File.WriteAllText(System.IO.Path.Combine(morningPath, "Project02.elvx"), "<Project />");
        File.WriteAllText(System.IO.Path.Combine(morningPath, "Project01_elvx.csv"), "morning 1");
        File.WriteAllText(System.IO.Path.Combine(morningPath, "Project02_elvx.csv"), "morning 2");
        File.WriteAllText(System.IO.Path.Combine(morningPath, "batch_results.csv"), "morning batch");
        string preservedFilePath = System.IO.Path.Combine(morningPath, "keep.txt");
        File.WriteAllText(preservedFilePath, "do not delete");

        FakeLauncherService launcher = new();
        ElevateProcessingService service = new(launcher);

        ProcessingResult result = await service.RunAsync(
            copiesCount: 2,
            path: workspace.Path,
            buildingType: BuildingType.Office,
            includeLunchPeak: true);

        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(preservedFilePath));
        Assert.Equal("morning 1", File.ReadAllText(System.IO.Path.Combine(morningPath, "Project01_elvx.csv")));
        Assert.True(File.Exists(System.IO.Path.Combine(workspace.Path, "lunch", "Project01.elvx")));
        Assert.True(File.Exists(System.IO.Path.Combine(workspace.Path, "lunch", "Project02.elvx")));
        Assert.Single(launcher.OfficeCalls);
        Assert.Equal((workspace.Path, true), launcher.OfficeCalls[0]);
    }

    [Fact]
    public async Task RunAsync_OfficeMorningOnly_DoesNotDeleteExistingLunchFolder()
    {
        using TestWorkspace workspace = new();
        _ = workspace.CreateSampleElvx("Project01.elvx");
        string lunchPath = System.IO.Path.Combine(workspace.Path, "lunch");
        Directory.CreateDirectory(lunchPath);
        string lunchResultPath = System.IO.Path.Combine(lunchPath, "Project01_elvx.csv");
        File.WriteAllText(lunchResultPath, "lunch result");

        FakeLauncherService launcher = new();
        ElevateProcessingService service = new(launcher);

        ProcessingResult result = await service.RunAsync(
            copiesCount: 2,
            path: workspace.Path,
            buildingType: BuildingType.Office,
            includeLunchPeak: false);

        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(lunchResultPath));
        Assert.Equal("lunch result", File.ReadAllText(lunchResultPath));
        Assert.Single(launcher.OfficeCalls);
        Assert.Equal((workspace.Path, false), launcher.OfficeCalls[0]);
    }

    [Fact]
    public async Task RunAsync_OfficeFlow_WritesCompletedRunManifest()
    {
        using TestWorkspace workspace = new();
        _ = workspace.CreateSampleElvx("Project01.elvx");
        FakeLauncherService launcher = new();
        ElevateProcessingService service = new(launcher);

        ProcessingResult result = await service.RunAsync(
            copiesCount: 2,
            path: workspace.Path,
            buildingType: BuildingType.Office,
            includeLunchPeak: false);

        Assert.True(result.Success, result.Message);
        ElevateRunManifest manifest = ReadRunManifest(workspace.Path);

        Assert.Equal(ElevateRunManifestStatus.Completed, manifest.Status);
        Assert.Equal(BuildingType.Office, manifest.BuildingType);
        Assert.False(manifest.IncludeLunchPeak);
        Assert.Equal(2, manifest.CopiesCount);
        Assert.NotEmpty(manifest.RunId);
        Assert.NotNull(manifest.CompletedAtUtc);
        Assert.Collection(
            manifest.Steps,
            step => AssertRunStep(step, ElevateRunManifestStepNames.ValidateInputs, ElevateRunManifestStatus.Completed),
            step => AssertRunStep(step, ElevateRunManifestStepNames.PrepareAndRunElevate, ElevateRunManifestStatus.Completed),
            step => AssertRunStep(step, ElevateRunManifestStepNames.CollectArtifacts, ElevateRunManifestStatus.Completed));
        Assert.Contains(
            manifest.Artifacts,
            artifact =>
                artifact.Kind == ElevateRunManifestArtifactKinds.FloorArea &&
                artifact.Scenario == "morning" &&
                System.IO.Path.GetFileName(artifact.Path).Equals("floor_area.csv", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(manifest.Artifacts, artifact => artifact.Scenario == "lunch");
    }

    [Fact]
    public async Task RunAsync_CanceledRun_WritesStoppedRunManifest()
    {
        using TestWorkspace workspace = new();
        _ = workspace.CreateSampleElvx("Project01.elvx");
        FakeLauncherService launcher = new()
        {
            WaitForResidenceCancellation = true,
        };
        ElevateProcessingService service = new(launcher);
        using CancellationTokenSource cancellationTokenSource = new();

        cancellationTokenSource.CancelAfter(TimeSpan.FromMilliseconds(20));
        ProcessingResult result = await service.RunAsync(
            copiesCount: 1,
            path: workspace.Path,
            buildingType: BuildingType.Residence,
            includeLunchPeak: false,
            cancellationToken: cancellationTokenSource.Token);

        Assert.False(result.Success);
        Assert.Contains("stopped", result.Message, StringComparison.OrdinalIgnoreCase);

        ElevateRunManifest manifest = ReadRunManifest(workspace.Path);
        Assert.Equal(ElevateRunManifestStatus.Stopped, manifest.Status);
        Assert.NotNull(manifest.CompletedAtUtc);
        Assert.Contains(
            manifest.Steps,
            step =>
                step.Name == ElevateRunManifestStepNames.PrepareAndRunElevate &&
                step.Status == ElevateRunManifestStatus.Stopped);
    }

    [Fact]
    public async Task RunAsync_WritesRunHistorySnapshot()
    {
        using TestWorkspace workspace = new();
        _ = workspace.CreateSampleElvx("Project01.elvx");
        FakeLauncherService launcher = new();
        ElevateProcessingService service = new(launcher);

        ProcessingResult result = await service.RunAsync(
            copiesCount: 2,
            path: workspace.Path,
            buildingType: BuildingType.Office,
            includeLunchPeak: false);

        Assert.True(result.Success, result.Message);
        ElevateRunManifest manifest = ReadRunManifest(workspace.Path);
        IReadOnlyList<ElevateRunManifest> history = service.GetRunHistory(workspace.Path);

        Assert.Single(history);
        Assert.Equal(manifest.RunId, history[0].RunId);
        Assert.True(File.Exists(ElevateRunManifestService.GetHistoryManifestPath(workspace.Path, manifest.RunId)));
    }

    [Fact]
    public async Task RunAsync_ResidenceFlow_UsesLauncherAndGeneratesCsv()
    {
        using TestWorkspace workspace = new();
        _ = workspace.CreateSampleElvx("Project01.elvx");
        FakeLauncherService launcher = new();
        ElevateProcessingService service = new(launcher);

        ProcessingResult result = await service.RunAsync(
            copiesCount: 2,
            path: workspace.Path,
            buildingType: BuildingType.Residence,
            includeLunchPeak: true);

        Assert.True(result.Success, result.Message);
        Assert.Single(launcher.ResidenceCalls);
        Assert.Equal(workspace.Path, launcher.ResidenceCalls[0]);
        Assert.True(File.Exists(System.IO.Path.Combine(workspace.Path, "floor_area.csv")));
    }

    [Fact]
    public async Task RunAsync_ResidenceFlow_SupportsBaseFileWithoutNumericSuffix()
    {
        using TestWorkspace workspace = new();
        _ = workspace.CreateSampleElvx("Tower.elvx");
        FakeLauncherService launcher = new();
        ElevateProcessingService service = new(launcher);

        ProcessingResult result = await service.RunAsync(
            copiesCount: 2,
            path: workspace.Path,
            buildingType: BuildingType.Residence,
            includeLunchPeak: true);

        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(System.IO.Path.Combine(workspace.Path, "Tower2.elvx")));
        Assert.Single(launcher.ResidenceCalls);
    }

    [Fact]
    public async Task RunAsync_ResidenceFlow_RemovesStaleGeneratedArtifacts()
    {
        using TestWorkspace workspace = new();
        _ = workspace.CreateSampleElvx("Project01.elvx");
        File.WriteAllText(System.IO.Path.Combine(workspace.Path, "Project9.elvx"), "<Project />");
        File.WriteAllText(System.IO.Path.Combine(workspace.Path, ".elevate-helper.generated-copies.txt"), "Project9.elvx");
        File.WriteAllText(System.IO.Path.Combine(workspace.Path, "batch_results.csv"), "old");
        File.WriteAllText(System.IO.Path.Combine(workspace.Path, "Project9_elvx.csv"), "old");
        File.WriteAllText(System.IO.Path.Combine(workspace.Path, "Project9.elvr"), "old");

        FakeLauncherService launcher = new();
        ElevateProcessingService service = new(launcher);

        ProcessingResult result = await service.RunAsync(
            copiesCount: 2,
            path: workspace.Path,
            buildingType: BuildingType.Residence,
            includeLunchPeak: true);

        Assert.True(result.Success, result.Message);
        Assert.False(File.Exists(System.IO.Path.Combine(workspace.Path, "Project9.elvx")));
        Assert.False(File.Exists(System.IO.Path.Combine(workspace.Path, "batch_results.csv")));
        Assert.False(File.Exists(System.IO.Path.Combine(workspace.Path, "Project9_elvx.csv")));
        Assert.False(File.Exists(System.IO.Path.Combine(workspace.Path, "Project9.elvr")));
        Assert.True(File.Exists(System.IO.Path.Combine(workspace.Path, "Project02.elvx")));
    }

    [Fact]
    public async Task RunAsync_ResidenceFlow_DoesNotOverwriteUntrackedElvxFiles()
    {
        using TestWorkspace workspace = new();
        _ = workspace.CreateSampleElvx("Project01.elvx");
        _ = workspace.CreateSampleElvx("Project02.elvx");

        FakeLauncherService launcher = new();
        ElevateProcessingService service = new(launcher);

        ProcessingResult result = await service.RunAsync(
            copiesCount: 2,
            path: workspace.Path,
            buildingType: BuildingType.Residence,
            includeLunchPeak: true);

        Assert.False(result.Success);
        Assert.Contains("Cannot overwrite existing .elvx file", result.Message, StringComparison.Ordinal);
        Assert.Empty(launcher.ResidenceCalls);
    }

    [Fact]
    public async Task RunAsync_ResidenceFlow_WritesFailedRunManifestOnCopyConflict()
    {
        using TestWorkspace workspace = new();
        _ = workspace.CreateSampleElvx("Project01.elvx");
        _ = workspace.CreateSampleElvx("Project02.elvx");

        FakeLauncherService launcher = new();
        ElevateProcessingService service = new(launcher);

        ProcessingResult result = await service.RunAsync(
            copiesCount: 2,
            path: workspace.Path,
            buildingType: BuildingType.Residence,
            includeLunchPeak: true);

        Assert.False(result.Success);
        ElevateRunManifest manifest = ReadRunManifest(workspace.Path);

        Assert.Equal(ElevateRunManifestStatus.Failed, manifest.Status);
        Assert.NotNull(manifest.CompletedAtUtc);
        Assert.Contains("Cannot overwrite existing .elvx file", manifest.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(
            manifest.Steps,
            step =>
                step.Name == ElevateRunManifestStepNames.PrepareAndRunElevate &&
                step.Status == ElevateRunManifestStatus.Failed &&
                step.ErrorMessage?.Contains("Cannot overwrite existing .elvx file", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(manifest.Steps, step => step.Name == ElevateRunManifestStepNames.CollectArtifacts);
    }

    [Fact]
    public async Task RetryLastFailedRunAsync_RerunsLatestFailedManifest()
    {
        using TestWorkspace workspace = new();
        _ = workspace.CreateSampleElvx("Project01.elvx");
        string conflictPath = workspace.CreateSampleElvx("Project02.elvx");

        FakeLauncherService launcher = new();
        ElevateProcessingService service = new(launcher);

        ProcessingResult failedResult = await service.RunAsync(
            copiesCount: 2,
            path: workspace.Path,
            buildingType: BuildingType.Residence,
            includeLunchPeak: true);
        File.Delete(conflictPath);

        ProcessingResult retryResult = await service.RetryLastFailedRunAsync(workspace.Path);

        Assert.False(failedResult.Success);
        Assert.True(retryResult.Success, retryResult.Message);
        Assert.Single(launcher.ResidenceCalls);

        ElevateRunManifest currentManifest = ReadRunManifest(workspace.Path);
        IReadOnlyList<ElevateRunManifest> history = service.GetRunHistory(workspace.Path);

        Assert.Equal(ElevateRunManifestStatus.Completed, currentManifest.Status);
        Assert.Equal(2, history.Count);
        Assert.Contains(history, manifest => manifest.Status == ElevateRunManifestStatus.Failed);
        Assert.Contains(history, manifest => manifest.Status == ElevateRunManifestStatus.Completed);
    }

    [Fact]
    public async Task RetryLastFailedRunAsync_ReturnsFailWhenLatestRunSucceeded()
    {
        using TestWorkspace workspace = new();
        _ = workspace.CreateSampleElvx("Project01.elvx");
        FakeLauncherService launcher = new();
        ElevateProcessingService service = new(launcher);

        ProcessingResult result = await service.RunAsync(
            copiesCount: 2,
            path: workspace.Path,
            buildingType: BuildingType.Office,
            includeLunchPeak: false);

        ProcessingResult retryResult = await service.RetryLastFailedRunAsync(workspace.Path);

        Assert.True(result.Success, result.Message);
        Assert.False(retryResult.Success);
        Assert.Contains("Last Elevate run is not failed", retryResult.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_InvalidPath_ReturnsFailResult()
    {
        ElevateProcessingService service = new(new FakeLauncherService());

        ProcessingResult result = await service.RunAsync(
            path: "X:\\this\\folder\\does\\not\\exist",
            buildingType: BuildingType.Office,
            includeLunchPeak: true);

        Assert.False(result.Success);
        Assert.Contains("Path does not exist", result.Message, StringComparison.Ordinal);
    }

    private static ElevateRunManifest ReadRunManifest(string path)
    {
        string manifestPath = ElevateRunManifestService.GetManifestPath(path);
        Assert.True(File.Exists(manifestPath), $"Expected run manifest at {manifestPath}.");

        return JsonSerializer.Deserialize<ElevateRunManifest>(File.ReadAllText(manifestPath))
            ?? throw new InvalidOperationException("Run manifest could not be deserialized.");
    }

    private static void AssertRunStep(
        ElevateRunManifestStep step,
        string expectedName,
        string expectedStatus)
    {
        Assert.Equal(expectedName, step.Name);
        Assert.Equal(expectedStatus, step.Status);
        Assert.NotNull(step.StartedAtUtc);
        Assert.NotNull(step.CompletedAtUtc);
    }

    private sealed class FakeLauncherService : IElevateLauncherService
    {
        public List<string> ResidenceCalls { get; } = [];
        public List<(string Path, bool IncludeLunchPeak)> OfficeCalls { get; } = [];

        public bool WaitForResidenceCancellation { get; init; }

        public Task LaunchResidenceAsync(string path, CancellationToken cancellationToken = default)
        {
            ResidenceCalls.Add(path);
            return WaitForResidenceCancellation
                ? WaitForCancellationAsync(cancellationToken)
                : Task.CompletedTask;
        }

        public Task LaunchOfficeAsync(
            string path,
            bool includeLunchPeak,
            CancellationToken cancellationToken = default)
        {
            OfficeCalls.Add((path, includeLunchPeak));
            return Task.CompletedTask;
        }

        private static async Task WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            TaskCompletionSource<bool> waitTask = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = cancellationToken.Register(
                static state => ((TaskCompletionSource<bool>)state!).TrySetCanceled(),
                waitTask);

            await waitTask.Task;
        }
    }

    private sealed class TestWorkspace : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "ElevateHelperTests",
            Guid.NewGuid().ToString("N"));

        public TestWorkspace()
        {
            Directory.CreateDirectory(Path);
        }

        public string CreateSampleElvx(string fileName)
        {
            string filePath = System.IO.Path.Combine(Path, fileName);
            XDocument xml = BuildSampleXml();
            xml.Save(filePath);
            return filePath;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // Keep test cleanup best-effort.
            }
        }

        private static XDocument BuildSampleXml()
        {
            return new XDocument(
                new XElement("Project",
                    new XElement("JobData", new XAttribute("JobTitle", "Test Project")),
                    new XElement("BuildingData", new XAttribute("BuildingType", "1")),
                    new XElement("PassengerData",
                        new XElement("Standard",
                            new XElement("HandlingCapacity", "1")),
                        new XElement("Traffic",
                            new XElement("Period",
                                new XAttribute("Id", "0"),
                                new XAttribute("TotalArrivalRate", "1"),
                                new XAttribute("SplitUp", "0"),
                                new XAttribute("SplitDown", "0"),
                                new XAttribute("SplitInterfloor", "0")))),
                    new XElement("ElevatorData",
                        new XElement("Advanced",
                            new XElement("Configuration",
                                new XElement("Car",
                                    new XAttribute("Id", "1"),
                                    new XAttribute("FloorAreaM2", "10.5")),
                                new XElement("Car",
                                    new XAttribute("Id", "2"),
                                    new XAttribute("FloorAreaM2", "12")))))));
        }
    }
}
