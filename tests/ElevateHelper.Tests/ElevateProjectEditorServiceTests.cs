using System.Globalization;
using System.Xml.Linq;
using ElevateHelperWinUI.Models;
using ElevateHelperWinUI.Services;

namespace ElevateHelper.Tests;

public sealed class ElevateProjectEditorServiceTests
{
    private readonly ElevateProjectEditorService service = new();

    [Fact]
    public async Task LoadTemplate_LoadsOfficeTemplateSections()
    {
        ElevateProjectEditorDocument document = await service.LoadTemplate(BuildingType.Office);

        Assert.Equal(BuildingType.Office, document.BuildingType);
        Assert.Equal("Офис", document.Job.Title);
        Assert.Equal("R00", document.Job.Number);
        Assert.Equal("Mixed Control (Enhanced ACA)", document.Analysis.DispatcherAlgorithmName);
        Assert.Equal("Up peak", document.Analysis.TrafficMode);
        Assert.True(document.Floors.Count > 0);
        Assert.True(document.Cars.Count > 0);
        Assert.EndsWith("Office.elvx", document.TemplatePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadFile_LoadsExistingElvxAndSuggestFileName()
    {
        string filePath = Path.Combine(GetExampleDirectory(), "Residential.elvx");

        ElevateProjectEditorDocument document = await service.LoadFile(filePath);
        string fileName = service.SuggestFileName(document);

        Assert.Equal(filePath, document.SourcePath);
        Assert.Equal(BuildingType.Residence, document.BuildingType);
        Assert.EndsWith("01.elvx", fileName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("R00", fileName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveAsync_PatchesLoadedDocumentAndPreservesOtherXml()
    {
        using ProjectEditorWorkspace workspace = new();
        string sourcePath = Path.Combine(workspace.RootPath, "Editable.elvx");
        File.Copy(Path.Combine(GetExampleDirectory(), "Office.elvx"), sourcePath);

        ElevateProjectEditorDocument document = await service.LoadFile(sourcePath);
        document.Job.Title = "Tower Alpha";
        document.Job.Number = "R42";
        document.Job.CalculationTitle = "Moscow";
        document.Job.MadeBy = "Codex";
        document.Job.CheckedBy = "QA";
        document.Job.Company = "Meteor";
        document.Analysis.DispatcherAlgorithmName = "Destination Control";
        document.Analysis.TrafficMode = "Lunch peak";
        document.Analysis.SimulationsPerConfiguration = 25;
        document.Analysis.LearningRuns = 2;
        document.Analysis.RandomSeed = 7;
        document.Building.BuildingType = BuildingType.Hotel;
        document.Building.AbsenteeismPercent = 12.5;
        document.Traffic.IncomingPercent = 45;
        document.Traffic.OutgoingPercent = 40;
        document.Traffic.InterfloorPercent = 15;
        document.Traffic.HandlingCapacity = 13.5;
        document.Traffic.LoadingTimeSeconds = 1.25;
        document.Traffic.UnloadingTimeSeconds = 1.5;
        document.Floors[0].InterfloorHeight = 9.75;
        document.Floors[0].Population = 123;
        document.Cars[0].CapacityKg = "1600.000000";
        document.Cars[0].DoorOpenTime = "2.100000";

        string outputPath = Path.Combine(workspace.RootPath, "Output.elvx");
        ProcessingResult result = await service.SaveAsync(document, outputPath);

        Assert.True(result.Success);
        Assert.True(File.Exists(outputPath));

        XDocument xDocument = XDocument.Load(outputPath);
        XElement root = xDocument.Root!;
        Assert.Equal("Tower Alpha", (string?)root.Element("JobData")?.Attribute("JobTitle"));
        Assert.Equal("R42", (string?)root.Element("JobData")?.Attribute("JobNo"));
        Assert.Equal("Destination Control", (string?)root.Element("AnalysisData")?.Element("Dispatcher")?.Element("Algorithm")?.Attribute("AlgorithmName"));
        Assert.Equal("Lunch peak", (string?)root.Element("AnalysisData")?.Element("Dispatcher")?.Element("Algorithm")?.Attribute("Mode"));
        Assert.Equal("25", (string?)root.Element("AnalysisData")?.Element("SimulationParameters")?.Attribute("NoOfSimulationsToRunForEachConfiguration"));
        Assert.Equal("2", (string?)root.Element("AnalysisData")?.Element("SimulationParameters")?.Attribute("NoOfLearningRuns"));
        Assert.Equal("7", (string?)root.Element("AnalysisData")?.Element("SimulationParameters")?.Attribute("RandomNumberSeedForPassengerGenerator"));
        Assert.Equal("2", (string?)root.Element("BuildingData")?.Attribute("BuildingType"));
        Assert.Equal("12.500000", (string?)root.Element("BuildingData")?.Attribute("AbsenteeismPercent"));
        Assert.Equal("45.000000", root.Element("PassengerData")?.Element("Standard")?.Element("Incoming")?.Value);
        Assert.Equal("13.500000", root.Element("PassengerData")?.Element("Standard")?.Element("HandlingCapacity")?.Value);
        Assert.Equal("1.250000", (string?)root.Element("PassengerData")?.Element("Standard")?.Attribute("LoadingTime"));
        Assert.Equal("45.000000", (string?)root.Element("PassengerData")?.Element("Traffic")?.Element("Period")?.Attribute("SplitUp"));
        Assert.Equal("9.750000", (string?)root.Element("BuildingData")?.Elements("Floor").First().Attribute("FloorLevel"));
        Assert.Equal("123.000000", (string?)root.Element("BuildingData")?.Elements("Floor").First().Attribute("NoOfPeople"));
        Assert.Equal("1600.000000", (string?)root.Element("ElevatorData")?.Element("Advanced")?.Element("Configuration")?.Elements("Car").First().Attribute("Capacity"));
        Assert.Equal("2.100000", (string?)root.Element("ElevatorData")?.Element("Advanced")?.Element("Configuration")?.Elements("Car").First().Attribute("DoorOpenTime"));
        Assert.NotNull(root.Element("Results"));
    }

    [Fact]
    public async Task SaveAsync_RebuildsConfigurationWhenLiftCountChanges()
    {
        using ProjectEditorWorkspace workspace = new();
        string sourcePath = Path.Combine(workspace.RootPath, "Editable.elvx");
        File.Copy(Path.Combine(GetExampleDirectory(), "Office.elvx"), sourcePath);

        ElevateProjectEditorDocument document = await service.LoadFile(sourcePath);
        ElevateProjectEditorCar baselineCar = document.Cars[0];
        document.Cars =
        [
            baselineCar,
            new ElevateProjectEditorCar
            {
                Id = "2",
                CapacityKg = baselineCar.CapacityKg,
                FloorAreaM2 = baselineCar.FloorAreaM2,
                Speed = baselineCar.Speed,
                Acceleration = baselineCar.Acceleration,
                Jerk = baselineCar.Jerk,
                DoorPreOpening = baselineCar.DoorPreOpening,
                DoorOpenTime = baselineCar.DoorOpenTime,
                DoorCloseTime = baselineCar.DoorCloseTime,
                HomeFloor = baselineCar.HomeFloor,
            },
            new ElevateProjectEditorCar
            {
                Id = "3",
                CapacityKg = baselineCar.CapacityKg,
                FloorAreaM2 = baselineCar.FloorAreaM2,
                Speed = baselineCar.Speed,
                Acceleration = baselineCar.Acceleration,
                Jerk = baselineCar.Jerk,
                DoorPreOpening = baselineCar.DoorPreOpening,
                DoorOpenTime = baselineCar.DoorOpenTime,
                DoorCloseTime = baselineCar.DoorCloseTime,
                HomeFloor = baselineCar.HomeFloor,
            },
            new ElevateProjectEditorCar
            {
                Id = "4",
                CapacityKg = baselineCar.CapacityKg,
                FloorAreaM2 = baselineCar.FloorAreaM2,
                Speed = baselineCar.Speed,
                Acceleration = baselineCar.Acceleration,
                Jerk = baselineCar.Jerk,
                DoorPreOpening = baselineCar.DoorPreOpening,
                DoorOpenTime = baselineCar.DoorOpenTime,
                DoorCloseTime = baselineCar.DoorCloseTime,
                HomeFloor = baselineCar.HomeFloor,
            },
            new ElevateProjectEditorCar
            {
                Id = "5",
                CapacityKg = baselineCar.CapacityKg,
                FloorAreaM2 = baselineCar.FloorAreaM2,
                Speed = baselineCar.Speed,
                Acceleration = baselineCar.Acceleration,
                Jerk = baselineCar.Jerk,
                DoorPreOpening = baselineCar.DoorPreOpening,
                DoorOpenTime = baselineCar.DoorOpenTime,
                DoorCloseTime = baselineCar.DoorCloseTime,
                HomeFloor = baselineCar.HomeFloor,
            },
        ];

        string outputPath = Path.Combine(workspace.RootPath, "Rebuilt.elvx");
        ProcessingResult result = await service.SaveAsync(document, outputPath);

        Assert.True(result.Success);

        XDocument xDocument = XDocument.Load(outputPath);
        XElement? configuration = xDocument.Root?.Element("ElevatorData")?.Element("Advanced")?.Element("Configuration");
        Assert.NotNull(configuration);
        Assert.Equal("5", (string?)configuration?.Attribute("NoOfLifts"));
        Assert.Equal("5", (string?)xDocument.Root?.Element("PassengerData")?.Element("Standard")?.Attribute("NoOfLifts"));
        Assert.Equal(5, configuration?.Elements("Car").Count());
        Assert.All(configuration!.Elements("Car"), car =>
        {
            Assert.Equal(document.Floors.Count, car.Elements("FloorServed").Count());
        });
        Assert.Equal("5", (string?)configuration.Elements("Car").Last().Attribute("HomeShaft"));
    }

    [Fact]
    public async Task SaveAsync_PreservesNonFloorServedCarChildren()
    {
        using ProjectEditorWorkspace workspace = new();
        string sourcePath = Path.Combine(workspace.RootPath, "Editable.elvx");
        File.Copy(Path.Combine(GetExampleDirectory(), "Office.elvx"), sourcePath);

        ElevateProjectEditorDocument document = await service.LoadFile(sourcePath);
        document.Cars[0].ServedFloorIndexes = [1, 2, 3];

        string outputPath = Path.Combine(workspace.RootPath, "PreservedChildren.elvx");
        ProcessingResult result = await service.SaveAsync(document, outputPath);

        Assert.True(result.Success);

        XDocument xDocument = XDocument.Load(outputPath);
        XElement? firstCar = xDocument.Root?.Element("ElevatorData")?.Element("Advanced")?.Element("Configuration")?.Elements("Car").FirstOrDefault();
        Assert.NotNull(firstCar);
        Assert.NotEmpty(firstCar!.Elements("DriveEnergy"));
        Assert.Equal(3, firstCar.Elements("FloorServed").Count());
    }

    [Fact]
    public async Task SaveAsync_DoesNotFlattenHeterogeneousLiftSummaryToFirstCar()
    {
        using ProjectEditorWorkspace workspace = new();
        string sourcePath = Path.Combine(workspace.RootPath, "Editable.elvx");
        File.Copy(Path.Combine(GetExampleDirectory(), "Office.elvx"), sourcePath);

        ElevateProjectEditorDocument document = await service.LoadFile(sourcePath);
        string? originalCapacity = (string?)XDocument.Load(sourcePath).Root?.Element("PassengerData")?.Element("Standard")?.Attribute("Capacity");
        document.Cars[0].CapacityKg = "825.000000";
        document.Cars[1].CapacityKg = "1350.000000";

        string outputPath = Path.Combine(workspace.RootPath, "Heterogeneous.elvx");
        ProcessingResult result = await service.SaveAsync(document, outputPath);

        Assert.True(result.Success);

        XDocument xDocument = XDocument.Load(outputPath);
        XElement? standard = xDocument.Root?.Element("PassengerData")?.Element("Standard");
        Assert.NotNull(standard);
        Assert.Equal("4", (string?)standard!.Attribute("NoOfLifts"));
        Assert.Equal(originalCapacity, (string?)standard.Attribute("Capacity"));
    }

    [Fact]
    public async Task SaveAsync_UpdatesFloorNamesAcrossConfigurationAndSpeedFill()
    {
        using ProjectEditorWorkspace workspace = new();
        string sourcePath = Path.Combine(workspace.RootPath, "Editable.elvx");
        File.Copy(Path.Combine(GetExampleDirectory(), "Office.elvx"), sourcePath);

        ElevateProjectEditorDocument document = await service.LoadFile(sourcePath);
        document.Floors[1].FloorName = "Lobby";

        string outputPath = Path.Combine(workspace.RootPath, "Renamed.elvx");
        ProcessingResult result = await service.SaveAsync(document, outputPath);

        Assert.True(result.Success);

        XDocument xDocument = XDocument.Load(outputPath);
        XElement root = xDocument.Root!;
        XElement renamedFloor = root.Element("BuildingData")!.Elements("Floor").ElementAt(1);
        Assert.Equal("Lobby", (string?)renamedFloor.Attribute("FloorName"));

        XElement firstCarServedFloor = root
            .Element("ElevatorData")!
            .Element("Advanced")!
            .Element("Configuration")!
            .Elements("Car")
            .First()
            .Elements("FloorServed")
            .First(floor => (string?)floor.Attribute("FloorIndex") == "2");
        Assert.Equal("Lobby", (string?)firstCarServedFloor.Attribute("FloorName"));

        Assert.Contains(
            root.Descendants("Series"),
            series => string.Equals((string?)series.Attribute("Data"), "Lobby", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SaveAsync_RebuildsBuildingFloorsWhenNewFloorIsAdded()
    {
        using ProjectEditorWorkspace workspace = new();
        string sourcePath = Path.Combine(workspace.RootPath, "Editable.elvx");
        File.Copy(Path.Combine(GetExampleDirectory(), "Office.elvx"), sourcePath);

        ElevateProjectEditorDocument document = await service.LoadFile(sourcePath);
        string originalFirstFloorName = document.Floors[0].FloorName;
        document.Floors.Insert(0, new ElevateProjectEditorFloor
        {
            FloorIndex = 1,
            SourceFloorName = string.Empty,
            FloorName = "Level -2",
            InterfloorHeight = 3.9,
            FloorLevel = 3.9,
            Population = 0,
            EntranceFloor = false,
        });

        foreach (ElevateProjectEditorFloor floor in document.Floors.Skip(1))
        {
            floor.SourceFloorName = floor.FloorName;
        }

        foreach (ElevateProjectEditorCar car in document.Cars)
        {
            car.HomeFloor = (int.Parse(car.HomeFloor, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);
            car.ServedFloorIndexes = car.ServedFloorIndexes.Select(index => index + 1).Prepend(1).ToList();
        }

        string outputPath = Path.Combine(workspace.RootPath, "AddedFloor.elvx");
        ProcessingResult result = await service.SaveAsync(document, outputPath);

        Assert.True(result.Success);

        XDocument xDocument = XDocument.Load(outputPath);
        XElement buildingData = xDocument.Root!.Element("BuildingData")!;
        List<XElement> floors = buildingData.Elements("Floor").ToList();
        Assert.Equal(document.Floors.Count, floors.Count);
        Assert.Equal(document.Floors.Count.ToString(CultureInfo.InvariantCulture), (string?)buildingData.Attribute("NoOfFloors"));
        Assert.Equal("Level -2", (string?)floors[0].Attribute("FloorName"));
        Assert.Equal("3.900000", (string?)floors[0].Attribute("FloorLevel"));
        Assert.Equal(originalFirstFloorName, (string?)floors[1].Attribute("FloorName"));
    }

    [Fact]
    public async Task SaveAsync_UsesSourceFloorNameMappingWhenNewBottomFloorIsInserted()
    {
        using ProjectEditorWorkspace workspace = new();
        string sourcePath = Path.Combine(workspace.RootPath, "Editable.elvx");
        File.Copy(Path.Combine(GetExampleDirectory(), "Office.elvx"), sourcePath);

        ElevateProjectEditorDocument document = await service.LoadFile(sourcePath);
        string originalFirstFloorName = document.Floors[0].FloorName;
        document.Floors.Insert(0, new ElevateProjectEditorFloor
        {
            FloorIndex = 1,
            SourceFloorName = string.Empty,
            FloorName = "Level -2",
            InterfloorHeight = 3.9,
            FloorLevel = 3.9,
            Population = 0,
            EntranceFloor = false,
        });
        document.Floors[1].SourceFloorName = originalFirstFloorName;
        document.Floors[1].FloorName = "Lobby";

        foreach (ElevateProjectEditorFloor floor in document.Floors.Skip(2))
        {
            floor.SourceFloorName = floor.FloorName;
        }

        foreach (ElevateProjectEditorCar car in document.Cars)
        {
            car.HomeFloor = (int.Parse(car.HomeFloor, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);
            car.ServedFloorIndexes = car.ServedFloorIndexes.Select(index => index + 1).Prepend(1).ToList();
        }

        string outputPath = Path.Combine(workspace.RootPath, "InsertedBelow.elvx");
        ProcessingResult result = await service.SaveAsync(document, outputPath);

        Assert.True(result.Success);

        XDocument xDocument = XDocument.Load(outputPath);
        XElement root = xDocument.Root!;
        Assert.Contains(
            root.Descendants("Series"),
            series => string.Equals((string?)series.Attribute("Data"), "Lobby", StringComparison.Ordinal));
        Assert.DoesNotContain(
            root.Descendants("Series"),
            series => string.Equals((string?)series.Attribute("Data"), originalFirstFloorName, StringComparison.Ordinal));
        Assert.Contains(
            root.Descendants("From"),
            from => string.Equals((string?)from.Attribute("FloorName"), "Lobby", StringComparison.Ordinal));
        Assert.Contains(
            root.Descendants("To"),
            to => string.Equals((string?)to.Attribute("FloorName"), "Lobby", StringComparison.Ordinal));
        Assert.Contains(
            root.Descendants("Floor"),
            floor => string.Equals((string?)floor.Attribute("FloorName"), "Level -2", StringComparison.Ordinal) &&
                     floor.Parent?.Name == "XDispatch");

        XElement firstCarServedFloor = root
            .Element("ElevatorData")!
            .Element("Advanced")!
            .Element("Configuration")!
            .Elements("Car")
            .First()
            .Elements("FloorServed")
            .First(floor => (string?)floor.Attribute("FloorIndex") == "2");
        Assert.Equal("Lobby", (string?)firstCarServedFloor.Attribute("FloorName"));
    }

    [Fact]
    public async Task SaveAsync_PreservesPerCarNestedChildren()
    {
        using ProjectEditorWorkspace workspace = new();
        string sourcePath = Path.Combine(workspace.RootPath, "Editable.elvx");
        File.Copy(Path.Combine(GetExampleDirectory(), "Office.elvx"), sourcePath);

        ElevateProjectEditorDocument document = await service.LoadFile(sourcePath);
        document.Cars[0].CapacityKg = "1800.000000";

        string outputPath = Path.Combine(workspace.RootPath, "Preserved.elvx");
        ProcessingResult result = await service.SaveAsync(document, outputPath);

        Assert.True(result.Success);

        XDocument xDocument = XDocument.Load(outputPath);
        XElement firstCar = xDocument.Root!
            .Element("ElevatorData")!
            .Element("Advanced")!
            .Element("Configuration")!
            .Elements("Car")
            .First();

        Assert.NotEmpty(firstCar.Elements("DriveEnergy"));
        Assert.Equal("1800.000000", (string?)firstCar.Attribute("Capacity"));
    }

    private static string GetExampleDirectory()
    {
        DirectoryInfo current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string examplePath = Path.Combine(current.FullName, ".example");
            if (Directory.Exists(examplePath))
            {
                return examplePath;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not find .example directory for tests.");
    }

    private sealed class ProjectEditorWorkspace : IDisposable
    {
        public string RootPath { get; } = Path.Combine(
            Path.GetTempPath(),
            "ElevateProjectEditorTests",
            Guid.NewGuid().ToString("N"));

        public ProjectEditorWorkspace()
        {
            Directory.CreateDirectory(RootPath);
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
                // Best effort cleanup.
            }
        }
    }
}
