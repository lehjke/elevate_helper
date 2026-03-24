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

    private sealed class FakeLauncherService : IElevateLauncherService
    {
        public List<string> ResidenceCalls { get; } = [];
        public List<(string Path, bool IncludeLunchPeak)> OfficeCalls { get; } = [];

        public Task LaunchResidenceAsync(string path, CancellationToken cancellationToken = default)
        {
            ResidenceCalls.Add(path);
            return Task.CompletedTask;
        }

        public Task LaunchOfficeAsync(
            string path,
            bool includeLunchPeak,
            CancellationToken cancellationToken = default)
        {
            OfficeCalls.Add((path, includeLunchPeak));
            return Task.CompletedTask;
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
