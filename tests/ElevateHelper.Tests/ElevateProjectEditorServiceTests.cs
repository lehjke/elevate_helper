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

    [Theory]
    [InlineData(null)]
    [InlineData("unsupported")]
    public async Task LoadFile_RejectsMissingOrUnknownBuildingType(string? buildingType)
    {
        using ProjectEditorWorkspace workspace = new();
        string sourcePath = Path.Combine(workspace.RootPath, "InvalidBuildingType.elvx");
        XDocument xDocument = XDocument.Load(Path.Combine(GetExampleDirectory(), "Office.elvx"));
        XAttribute attribute = xDocument.Root!.Element("BuildingData")!.Attribute("BuildingType")!;
        if (buildingType is null)
        {
            attribute.Remove();
        }
        else
        {
            attribute.Value = buildingType;
        }

        xDocument.Save(sourcePath);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoadFile(sourcePath));

        Assert.Contains("Unknown building type", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("SimulationParameters", "SimulationParameters")]
    [InlineData("Configuration", "Configuration")]
    [InlineData("Cars", "at least one Car")]
    public async Task LoadFile_RejectsMissingRequiredEditableStructure(string section, string expectedMessage)
    {
        using ProjectEditorWorkspace workspace = new();
        string sourcePath = Path.Combine(workspace.RootPath, "MissingStructure.elvx");
        XDocument xDocument = XDocument.Load(Path.Combine(GetExampleDirectory(), "Office.elvx"));
        XElement root = xDocument.Root!;
        XElement configuration = root.Element("ElevatorData")!.Element("Advanced")!.Element("Configuration")!;
        switch (section)
        {
            case "SimulationParameters":
                root.Element("AnalysisData")!.Element("SimulationParameters")!.Remove();
                break;
            case "Configuration":
                configuration.Remove();
                break;
            case "Cars":
                configuration.Elements("Car").Remove();
                break;
        }

        xDocument.Save(sourcePath);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoadFile(sourcePath));

        Assert.Contains(expectedMessage, error.Message, StringComparison.OrdinalIgnoreCase);
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
        document.Job.LogoFile = @"C:\Temp\logo.png";
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
        document.Floors[1].InterfloorHeight = 9.75;
        document.Floors[0].Population = 123;
        document.Floors[0].EntranceBiasPercent = 8.25;
        document.Floors[1].EntranceBiasPercent = 11.75;
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
        Assert.Equal(@"C:\Temp\logo.png", (string?)root.Element("JobData")?.Attribute("LogoFile"));
        Assert.Equal("Group Collective", (string?)root.Element("AnalysisData")?.Element("Dispatcher")?.Element("Algorithm")?.Attribute("AlgorithmName"));
        Assert.Equal("Lunch peak", (string?)root.Element("AnalysisData")?.Element("Dispatcher")?.Element("Algorithm")?.Attribute("Mode"));
        Assert.Equal("25", (string?)root.Element("AnalysisData")?.Element("SimulationParameters")?.Attribute("NoOfSimulationsToRunForEachConfiguration"));
        Assert.Equal("2", (string?)root.Element("AnalysisData")?.Element("SimulationParameters")?.Attribute("NoOfLearningRuns"));
        Assert.Equal("7", (string?)root.Element("AnalysisData")?.Element("SimulationParameters")?.Attribute("RandomNumberSeedForPassengerGenerator"));
        Assert.Equal("2", (string?)root.Element("BuildingData")?.Attribute("BuildingType"));
        Assert.Equal("0.000000", (string?)root.Element("BuildingData")?.Attribute("AbsenteeismPercent"));
        Assert.Equal("45.000000", root.Element("PassengerData")?.Element("Standard")?.Element("Incoming")?.Value);
        Assert.Equal("13.500000", root.Element("PassengerData")?.Element("Standard")?.Element("HandlingCapacity")?.Value);
        Assert.Equal("1.250000", (string?)root.Element("PassengerData")?.Element("Standard")?.Attribute("LoadingTime"));
        Assert.Equal("8.250000", (string?)root.Element("PassengerData")?.Element("Standard")?.Elements("Floor").First().Attribute("EntranceBias"));
        Assert.Equal("45.000000", (string?)root.Element("PassengerData")?.Element("Traffic")?.Element("Period")?.Attribute("SplitUp"));
        Assert.Equal("9.750000", (string?)root.Element("BuildingData")?.Elements("Floor").ElementAt(1).Attribute("FloorLevel"));
        Assert.Equal("123.000000", (string?)root.Element("BuildingData")?.Elements("Floor").First().Attribute("NoOfPeople"));
        Assert.Equal("1600.000000", (string?)root.Element("ElevatorData")?.Element("Advanced")?.Element("Configuration")?.Elements("Car").First().Attribute("Capacity"));
        Assert.Equal("1.900000", (string?)root.Element("ElevatorData")?.Element("Advanced")?.Element("Configuration")?.Elements("Car").First().Attribute("DoorOpenTime"));
        List<XElement> xDispatchFloors = root.Element("ElevatorData")?.Element("XDispatch")?.Elements("Floor").ToList() ?? [];
        Assert.NotEmpty(xDispatchFloors);
        Assert.All(xDispatchFloors, floor => Assert.Equal("False", (string?)floor.Attribute("DestinationButtons")));
        Assert.NotNull(root.Element("Results"));
    }

    [Fact]
    public async Task SaveAsync_ReplacesExistingOutputAtomicallyWithoutLeavingTemporaryFiles()
    {
        using ProjectEditorWorkspace workspace = new();
        string sourcePath = Path.Combine(workspace.RootPath, "Editable.elvx");
        string outputPath = Path.Combine(workspace.RootPath, "Existing.elvx");
        File.Copy(Path.Combine(GetExampleDirectory(), "Office.elvx"), sourcePath);
        await File.WriteAllTextAsync(outputPath, "unrelated previous content");

        ElevateProjectEditorDocument document = await service.LoadFile(sourcePath);
        document.Job.Title = "Atomic replacement";

        ProcessingResult result = await service.SaveAsync(document, outputPath);

        Assert.True(result.Success);
        Assert.Equal(
            "Atomic replacement",
            (string?)XDocument.Load(outputPath).Root?.Element("JobData")?.Attribute("JobTitle"));
        Assert.Empty(Directory.EnumerateFiles(workspace.RootPath, ".Existing.elvx.*.tmp"));
    }

    [Fact]
    public void SaveAtomically_WhenTemporaryWriteFails_PreservesExistingOutputAndCleansTemporaryFile()
    {
        using ProjectEditorWorkspace workspace = new();
        string outputPath = Path.Combine(workspace.RootPath, "Existing.elvx");
        File.WriteAllText(outputPath, "previous content");
        XDocument document = new(new XElement("Project"));

        IOException error = Assert.Throws<IOException>(() =>
            ElevateProjectEditorService.SaveAtomically(
                document,
                outputPath,
                (_, temporaryPath) =>
                {
                    File.WriteAllText(temporaryPath, "partial content");
                    throw new IOException("Injected temporary write failure.");
                }));

        Assert.Contains("Injected", error.Message, StringComparison.Ordinal);
        Assert.Equal("previous content", File.ReadAllText(outputPath));
        Assert.Empty(Directory.EnumerateFiles(workspace.RootPath, ".Existing.elvx.*.tmp"));
    }

    [Fact]
    public async Task SaveAsync_RejectsDuplicateFloorNames()
    {
        using ProjectEditorWorkspace workspace = new();
        string sourcePath = Path.Combine(workspace.RootPath, "Editable.elvx");
        File.Copy(Path.Combine(GetExampleDirectory(), "Office.elvx"), sourcePath);
        ElevateProjectEditorService service = new();
        ElevateProjectEditorDocument document = await service.LoadFile(sourcePath);
        document.Floors[1].FloorName = document.Floors[0].FloorName;
        string outputPath = Path.Combine(workspace.RootPath, "Invalid.elvx");

        ProcessingResult result = await service.SaveAsync(document, outputPath);

        Assert.False(result.Success);
        Assert.Contains("duplicated", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task SaveAsync_RejectsEntranceBiasThatDoesNotTotalOneHundredPercent()
    {
        using ProjectEditorWorkspace workspace = new();
        string sourcePath = Path.Combine(workspace.RootPath, "Editable.elvx");
        File.Copy(Path.Combine(GetExampleDirectory(), "Office.elvx"), sourcePath);
        ElevateProjectEditorService service = new();
        ElevateProjectEditorDocument document = await service.LoadFile(sourcePath);
        foreach (ElevateProjectEditorFloor floor in document.Floors.Where(floor => floor.EntranceFloor))
        {
            floor.EntranceBiasPercent = 0d;
        }

        document.Floors.First(floor => floor.EntranceFloor).EntranceBiasPercent = 50d;
        string outputPath = Path.Combine(workspace.RootPath, "Invalid.elvx");

        ProcessingResult result = await service.SaveAsync(document, outputPath);

        Assert.False(result.Success);
        Assert.Contains("total 100%", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task SaveAsync_RejectsHomeFloorOutsideBuildingRange()
    {
        using ProjectEditorWorkspace workspace = new();
        string sourcePath = Path.Combine(workspace.RootPath, "Editable.elvx");
        File.Copy(Path.Combine(GetExampleDirectory(), "Office.elvx"), sourcePath);
        ElevateProjectEditorDocument document = await service.LoadFile(sourcePath);
        document.Cars[0].HomeFloor = (document.Floors.Count + 1).ToString(CultureInfo.InvariantCulture);
        string outputPath = Path.Combine(workspace.RootPath, "Invalid.elvx");

        ProcessingResult result = await service.SaveAsync(document, outputPath);

        Assert.False(result.Success);
        Assert.Contains("home floor", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(outputPath));
    }

    [Theory]
    [InlineData(0, 1d, "base floor")]
    [InlineData(0, 0.01d, "base floor")]
    [InlineData(0, 0.001d, "base floor")]
    [InlineData(0, -0.001d, "base floor")]
    [InlineData(1, 0d, "greater than zero")]
    public async Task SaveAsync_RejectsNonIncreasingFloorLevels(
        int floorIndex,
        double interfloorHeight,
        string expectedMessage)
    {
        using ProjectEditorWorkspace workspace = new();
        string sourcePath = Path.Combine(workspace.RootPath, "Editable.elvx");
        File.Copy(Path.Combine(GetExampleDirectory(), "Office.elvx"), sourcePath);
        ElevateProjectEditorDocument document = await service.LoadFile(sourcePath);
        document.Floors[floorIndex].InterfloorHeight = interfloorHeight;
        string outputPath = Path.Combine(workspace.RootPath, "Invalid.elvx");

        ProcessingResult result = await service.SaveAsync(document, outputPath);

        Assert.False(result.Success);
        Assert.Contains(expectedMessage, result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(outputPath));
    }

    [Theory]
    [InlineData("capacity", "capacity")]
    [InlineData("speed", "speed")]
    [InlineData("home-shaft", "shaft")]
    public async Task SaveAsync_RejectsInvalidLiftGroupValues(string field, string expectedMessage)
    {
        using ProjectEditorWorkspace workspace = new();
        string sourcePath = Path.Combine(workspace.RootPath, "Editable.elvx");
        File.Copy(Path.Combine(GetExampleDirectory(), "Office.elvx"), sourcePath);
        ElevateProjectEditorDocument document = await service.LoadFile(sourcePath);
        switch (field)
        {
            case "capacity":
                document.Cars[0].CapacityKg = "0";
                break;
            case "speed":
                document.Cars[0].Speed = "NaN";
                break;
            case "home-shaft":
                document.Cars[1].HomeShaft = document.Cars[0].HomeShaft;
                break;
        }

        string outputPath = Path.Combine(workspace.RootPath, "Invalid.elvx");
        ProcessingResult result = await service.SaveAsync(document, outputPath);

        Assert.False(result.Success);
        Assert.Contains(expectedMessage, result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(outputPath));
    }

    [Theory]
    [InlineData("SimulationParameters")]
    [InlineData("Configuration")]
    public async Task SaveAsync_RejectsBaseDocumentThatLostRequiredEditableStructure(string section)
    {
        using ProjectEditorWorkspace workspace = new();
        string sourcePath = Path.Combine(workspace.RootPath, "Editable.elvx");
        File.Copy(Path.Combine(GetExampleDirectory(), "Office.elvx"), sourcePath);
        ElevateProjectEditorDocument document = await service.LoadFile(sourcePath);
        XDocument xDocument = XDocument.Load(sourcePath);
        if (section == "SimulationParameters")
        {
            xDocument.Root!.Element("AnalysisData")!.Element("SimulationParameters")!.Remove();
        }
        else
        {
            xDocument.Root!.Element("ElevatorData")!.Element("Advanced")!.Element("Configuration")!.Remove();
        }

        xDocument.Save(sourcePath);
        string outputPath = Path.Combine(workspace.RootPath, "ShouldNotExist.elvx");

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveAsync(document, outputPath));

        Assert.Contains(section, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task SaveAsync_RejectsNonPositiveSimulationCount()
    {
        using ProjectEditorWorkspace workspace = new();
        string sourcePath = Path.Combine(workspace.RootPath, "Editable.elvx");
        File.Copy(Path.Combine(GetExampleDirectory(), "Office.elvx"), sourcePath);
        ElevateProjectEditorDocument document = await service.LoadFile(sourcePath);
        document.Analysis.SimulationsPerConfiguration = 0;
        string outputPath = Path.Combine(workspace.RootPath, "Invalid.elvx");

        ProcessingResult result = await service.SaveAsync(document, outputPath);

        Assert.False(result.Success);
        Assert.Contains("simulation", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(outputPath));
    }

    [Theory]
    [InlineData(-1d, 100d, 1d, 12d, 1d, 1d, "between")]
    [InlineData(50d, 40d, 5d, 12d, 1d, 1d, "total")]
    [InlineData(50d, 50d, 0d, -1d, 1d, 1d, "negative")]
    [InlineData(50d, 50d, 0d, 12d, -1d, 1d, "negative")]
    public async Task SaveAsync_RejectsInvalidTrafficValues(
        double incoming,
        double outgoing,
        double interfloor,
        double handlingCapacity,
        double loadingTime,
        double unloadingTime,
        string expectedMessage)
    {
        using ProjectEditorWorkspace workspace = new();
        string sourcePath = Path.Combine(workspace.RootPath, "Editable.elvx");
        File.Copy(Path.Combine(GetExampleDirectory(), "Office.elvx"), sourcePath);
        ElevateProjectEditorDocument document = await service.LoadFile(sourcePath);
        document.Traffic.IncomingPercent = incoming;
        document.Traffic.OutgoingPercent = outgoing;
        document.Traffic.InterfloorPercent = interfloor;
        document.Traffic.HandlingCapacity = handlingCapacity;
        document.Traffic.LoadingTimeSeconds = loadingTime;
        document.Traffic.UnloadingTimeSeconds = unloadingTime;
        string outputPath = Path.Combine(workspace.RootPath, "Invalid.elvx");

        ProcessingResult result = await service.SaveAsync(document, outputPath);

        Assert.False(result.Success);
        Assert.Contains(expectedMessage, result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task SaveAsync_NormalizesOfficeDispatcherAndDestinationCalls()
    {
        using ProjectEditorWorkspace workspace = new();
        string sourcePath = Path.Combine(workspace.RootPath, "Office.elvx");
        File.Copy(Path.Combine(GetExampleDirectory(), "Office.elvx"), sourcePath);

        ElevateProjectEditorDocument document = await service.LoadFile(sourcePath);
        document.BuildingType = BuildingType.Office;
        document.Building.BuildingType = BuildingType.Office;
        document.Building.AbsenteeismPercent = 0;
        document.Analysis.DispatcherAlgorithmName = "Group Collective";

        string outputPath = Path.Combine(workspace.RootPath, "OfficeOutput.elvx");
        ProcessingResult result = await service.SaveAsync(document, outputPath);

        Assert.True(result.Success);

        XElement root = XDocument.Load(outputPath).Root!;
        Assert.Equal("Mixed Control (Enhanced ACA)", (string?)root.Element("AnalysisData")?.Element("Dispatcher")?.Element("Algorithm")?.Attribute("AlgorithmName"));
        Assert.Equal("20.000000", (string?)root.Element("BuildingData")?.Attribute("AbsenteeismPercent"));

        List<XElement> xDispatchFloors = root.Element("ElevatorData")?.Element("XDispatch")?.Elements("Floor").ToList() ?? [];
        Assert.NotEmpty(xDispatchFloors);
        Assert.All(xDispatchFloors, floor => Assert.Equal("True", (string?)floor.Attribute("DestinationButtons")));
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
                ServedFloorIndexes = [.. baselineCar.ServedFloorIndexes],
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
                ServedFloorIndexes = [.. baselineCar.ServedFloorIndexes],
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
                ServedFloorIndexes = [.. baselineCar.ServedFloorIndexes],
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
                ServedFloorIndexes = [.. baselineCar.ServedFloorIndexes],
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
    public async Task SaveAsync_SwapsFloorNamesWithoutCollapsingReferences()
    {
        using ProjectEditorWorkspace workspace = new();
        string sourcePath = Path.Combine(workspace.RootPath, "Editable.elvx");
        File.Copy(Path.Combine(GetExampleDirectory(), "Office.elvx"), sourcePath);
        XDocument sourceDocument = XDocument.Load(sourcePath);
        ElevateProjectEditorDocument document = await service.LoadFile(sourcePath);
        string firstName = document.Floors[0].FloorName;
        string secondName = document.Floors[1].FloorName;
        int sourceFirstSeriesCount = sourceDocument.Descendants("Series")
            .Count(series => (string?)series.Attribute("Data") == firstName);
        int sourceSecondSeriesCount = sourceDocument.Descendants("Series")
            .Count(series => (string?)series.Attribute("Data") == secondName);
        document.Floors[0].FloorName = secondName;
        document.Floors[1].FloorName = firstName;

        string outputPath = Path.Combine(workspace.RootPath, "Swapped.elvx");
        ProcessingResult result = await service.SaveAsync(document, outputPath);

        Assert.True(result.Success);
        XElement root = XDocument.Load(outputPath).Root!;
        List<XElement> buildingFloors = root.Element("BuildingData")!.Elements("Floor").ToList();
        Assert.Equal(secondName, (string?)buildingFloors[0].Attribute("FloorName"));
        Assert.Equal(firstName, (string?)buildingFloors[1].Attribute("FloorName"));

        List<XElement> dispatchFloors = root.Element("ElevatorData")!.Element("XDispatch")!.Elements("Floor").ToList();
        Assert.Equal(secondName, (string?)dispatchFloors[0].Attribute("FloorName"));
        Assert.Equal(firstName, (string?)dispatchFloors[1].Attribute("FloorName"));

        List<XElement> servedFloors = root.Element("ElevatorData")!
            .Element("Advanced")!
            .Element("Configuration")!
            .Elements("Car")
            .First()
            .Elements("FloorServed")
            .ToList();
        Assert.Equal(secondName, (string?)servedFloors[0].Attribute("FloorName"));
        Assert.Equal(firstName, (string?)servedFloors[1].Attribute("FloorName"));

        XElement passengerDemand = root.Element("PassengerData")!
            .Element("Advanced")!
            .Elements("Period")
            .First()
            .Element("PassengerDemand")!;
        Assert.Equal(secondName, (string?)passengerDemand.Elements("From").First().Attribute("FloorName"));
        Assert.Contains(
            passengerDemand.Elements("From").First().Elements("To"),
            destination => (string?)destination.Attribute("FloorName") == firstName);

        Assert.Equal(
            sourceSecondSeriesCount,
            root.Descendants("Series").Count(series => (string?)series.Attribute("Data") == firstName));
        Assert.Equal(
            sourceFirstSeriesCount,
            root.Descendants("Series").Count(series => (string?)series.Attribute("Data") == secondName));
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
            FloorName = "Level -4",
            InterfloorHeight = 0,
            FloorLevel = 0,
            Population = 0,
            EntranceFloor = false,
        });
        document.Floors[1].InterfloorHeight = 3.9;

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
        Assert.Equal("Level -4", (string?)floors[0].Attribute("FloorName"));
        Assert.Equal("0.000000", (string?)floors[0].Attribute("FloorLevel"));
        Assert.Equal("3.900000", (string?)floors[1].Attribute("FloorLevel"));
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
            FloorName = "Level -4",
            InterfloorHeight = 0,
            FloorLevel = 0,
            Population = 0,
            EntranceFloor = false,
        });
        document.Floors[1].InterfloorHeight = 3.9;
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
            floor => string.Equals((string?)floor.Attribute("FloorName"), "Level -4", StringComparison.Ordinal) &&
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
        DirectoryInfo? current = new(AppContext.BaseDirectory);
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
