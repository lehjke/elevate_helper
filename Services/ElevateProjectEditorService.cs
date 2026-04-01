using System.Globalization;
using System.Xml.Linq;
using ElevateHelperWinUI.Models;

namespace ElevateHelperWinUI.Services;

public sealed class ElevateProjectEditorService : IElevateProjectEditorService
{
    private const string OfficeTemplateFileName = "Office.elvx";
    private const string ResidenceTemplateFileName = "Residential.elvx";
    private const string HotelTemplateFileName = "Hotel.elvx";
    private static readonly LiftGroupRulesService LiftRules = new();

    public Task<ElevateProjectEditorDocument> LoadTemplate(
        BuildingType buildingType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string templatePath = GetTemplatePath(buildingType);
        XDocument document = LoadDocument(templatePath);
        ElevateProjectEditorDocument editorDocument = ParseDocument(document, templatePath, templatePath);
        return Task.FromResult(editorDocument);
    }

    public Task<ElevateProjectEditorDocument> LoadFile(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new InvalidOperationException("ELVX file path is empty.");
        }

        if (!File.Exists(filePath))
        {
            throw new InvalidOperationException("ELVX file was not found: " + filePath);
        }

        XDocument document = LoadDocument(filePath);
        BuildingType buildingType = ParseBuildingType(
            (string?)document.Root?.Element("BuildingData")?.Attribute("BuildingType"));
        string? templatePath = TryGetTemplatePath(buildingType);
        ElevateProjectEditorDocument editorDocument = ParseDocument(document, filePath, templatePath);
        return Task.FromResult(editorDocument);
    }

    public Task<ProcessingResult> SaveAsync(
        ElevateProjectEditorDocument document,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return Task.FromResult(ProcessingResult.Fail("Output path is empty."));
        }

        string basePath = ResolveBasePath(document);
        XDocument xDocument = LoadDocument(basePath);

        ApplyDocument(xDocument, document);
        string? outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        xDocument.Save(outputPath, SaveOptions.DisableFormatting);

        document.SourcePath = outputPath;
        return Task.FromResult(ProcessingResult.Ok("ELVX saved: " + outputPath));
    }

    public string SuggestFileName(ElevateProjectEditorDocument document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        string title = SanitizePathSegment(document.Job.Title, "Project");
        string jobNo = SanitizePathSegment(document.Job.Number, "R00");
        return $"{title} {jobNo} 01.elvx";
    }

    private static ElevateProjectEditorDocument ParseDocument(XDocument document, string? sourcePath, string? templatePath)
    {
        XElement root = document.Root
            ?? throw new InvalidOperationException("Project ELVX is empty.");
        XElement jobData = root.Element("JobData")
            ?? throw new InvalidOperationException("JobData section is missing.");
        XElement analysisData = root.Element("AnalysisData")
            ?? throw new InvalidOperationException("AnalysisData section is missing.");
        XElement buildingData = root.Element("BuildingData")
            ?? throw new InvalidOperationException("BuildingData section is missing.");
        XElement elevatorData = root.Element("ElevatorData")
            ?? throw new InvalidOperationException("ElevatorData section is missing.");
        XElement passengerData = root.Element("PassengerData")
            ?? throw new InvalidOperationException("PassengerData section is missing.");
        XElement algorithm = analysisData.Element("Dispatcher")?.Element("Algorithm")
            ?? throw new InvalidOperationException("Dispatcher algorithm section is missing.");
        XElement? simulationParameters = analysisData.Element("SimulationParameters");
        XElement standardPassenger = passengerData.Element("Standard")
            ?? throw new InvalidOperationException("PassengerData/Standard section is missing.");
        XElement? trafficPeriod = passengerData
            .Element("Traffic")?
            .Elements("Period")
            .FirstOrDefault(period => string.Equals((string?)period.Attribute("Id"), "0", StringComparison.Ordinal));
        XElement? configuration = elevatorData.Element("Advanced")?.Element("Configuration");

        BuildingType buildingType = ParseBuildingType((string?)buildingData.Attribute("BuildingType"));

        List<ElevateProjectEditorFloor> floors = ParseFloors(buildingData);
        List<ElevateProjectEditorCar> cars = ParseCars(configuration);

        return new ElevateProjectEditorDocument
        {
            SourcePath = sourcePath,
            TemplatePath = templatePath,
            BuildingType = buildingType,
            Job = new ElevateProjectEditorJobSection
            {
                Title = (string?)jobData.Attribute("JobTitle") ?? string.Empty,
                Number = (string?)jobData.Attribute("JobNo") ?? string.Empty,
                CalculationTitle = (string?)jobData.Attribute("CalculationTitle") ?? string.Empty,
                MadeBy = (string?)jobData.Attribute("MadeBy") ?? string.Empty,
                CheckedBy = (string?)jobData.Attribute("CheckedBy") ?? string.Empty,
                Company = (string?)jobData.Attribute("Company") ?? string.Empty,
                LogoFile = (string?)jobData.Attribute("LogoFile") ?? string.Empty,
            },
            Analysis = new ElevateProjectEditorAnalysisSection
            {
                DispatcherAlgorithmName = (string?)algorithm.Attribute("AlgorithmName") ?? string.Empty,
                TrafficMode = (string?)algorithm.Attribute("Mode") ?? string.Empty,
                SimulationsPerConfiguration = ParseInt((string?)simulationParameters?.Attribute("NoOfSimulationsToRunForEachConfiguration")),
                LearningRuns = ParseInt((string?)simulationParameters?.Attribute("NoOfLearningRuns")),
                RandomSeed = ParseInt((string?)simulationParameters?.Attribute("RandomNumberSeedForPassengerGenerator")),
            },
            Building = new ElevateProjectEditorBuildingSection
            {
                BuildingType = buildingType,
                AbsenteeismPercent = ParseDouble((string?)buildingData.Attribute("AbsenteeismPercent")),
                NumberOfFloors = ParseInt((string?)buildingData.Attribute("NoOfFloors")),
            },
            Traffic = new ElevateProjectEditorTrafficSection
            {
                IncomingPercent = ParseDouble(standardPassenger.Element("Incoming")?.Value, (string?)trafficPeriod?.Attribute("SplitUp")),
                OutgoingPercent = ParseDouble(standardPassenger.Element("Outgoing")?.Value, (string?)trafficPeriod?.Attribute("SplitDown")),
                InterfloorPercent = ParseDouble(standardPassenger.Element("Interfloor")?.Value, (string?)trafficPeriod?.Attribute("SplitInterfloor")),
                HandlingCapacity = ParseDouble(standardPassenger.Element("HandlingCapacity")?.Value),
                LoadingTimeSeconds = ParseDouble((string?)standardPassenger.Attribute("LoadingTime")),
                UnloadingTimeSeconds = ParseDouble((string?)standardPassenger.Attribute("UnloadingTime")),
            },
            Floors = floors,
            Cars = cars,
        };
    }

    private static void ApplyDocument(XDocument xDocument, ElevateProjectEditorDocument document)
    {
        XElement root = xDocument.Root
            ?? throw new InvalidOperationException("Project ELVX is empty.");
        XElement jobData = root.Element("JobData")
            ?? throw new InvalidOperationException("JobData section is missing.");
        XElement analysisData = root.Element("AnalysisData")
            ?? throw new InvalidOperationException("AnalysisData section is missing.");
        XElement buildingData = root.Element("BuildingData")
            ?? throw new InvalidOperationException("BuildingData section is missing.");
        XElement elevatorData = root.Element("ElevatorData")
            ?? throw new InvalidOperationException("ElevatorData section is missing.");
        XElement passengerData = root.Element("PassengerData")
            ?? throw new InvalidOperationException("PassengerData section is missing.");
        XElement algorithm = analysisData.Element("Dispatcher")?.Element("Algorithm")
            ?? throw new InvalidOperationException("Dispatcher algorithm section is missing.");
        XElement? simulationParameters = analysisData.Element("SimulationParameters");
        XElement standardPassenger = passengerData.Element("Standard")
            ?? throw new InvalidOperationException("PassengerData/Standard section is missing.");
        XElement? trafficPeriod = passengerData
            .Element("Traffic")?
            .Elements("Period")
            .FirstOrDefault(period => string.Equals((string?)period.Attribute("Id"), "0", StringComparison.Ordinal));
        XElement? configuration = elevatorData.Element("Advanced")?.Element("Configuration");

        SetAttribute(jobData, "JobTitle", document.Job.Title);
        SetAttribute(jobData, "JobNo", document.Job.Number);
        SetAttribute(jobData, "CalculationTitle", document.Job.CalculationTitle);
        SetAttribute(jobData, "MadeBy", document.Job.MadeBy);
        SetAttribute(jobData, "CheckedBy", document.Job.CheckedBy);
        SetAttribute(jobData, "Company", document.Job.Company);
        SetAttribute(jobData, "LogoFile", document.Job.LogoFile);

        SetAttribute(algorithm, "AlgorithmName", document.Analysis.DispatcherAlgorithmName);
        SetAttribute(algorithm, "Mode", document.Analysis.TrafficMode);
        if (simulationParameters is not null)
        {
            SetAttribute(simulationParameters, "NoOfSimulationsToRunForEachConfiguration", document.Analysis.SimulationsPerConfiguration.ToString(CultureInfo.InvariantCulture));
            SetAttribute(simulationParameters, "NoOfLearningRuns", document.Analysis.LearningRuns.ToString(CultureInfo.InvariantCulture));
            SetAttribute(simulationParameters, "RandomNumberSeedForPassengerGenerator", document.Analysis.RandomSeed.ToString(CultureInfo.InvariantCulture));
        }

        SetAttribute(buildingData, "BuildingType", ToBuildingTypeCode(document.Building.BuildingType));
        SetAttribute(buildingData, "AbsenteeismPercent", FormatDouble(document.Building.AbsenteeismPercent));
        SetAttribute(buildingData, "NoOfFloors", document.Floors.Count.ToString(CultureInfo.InvariantCulture));

        List<XElement> floorElements = buildingData.Elements("Floor").ToList();
        List<string> originalFloorNames = floorElements
            .Take(document.Floors.Count)
            .Select(floorElement => (string?)floorElement.Attribute("FloorName") ?? string.Empty)
            .ToList();
        double cumulativeFloorLevel = 0d;
        for (int index = 0; index < Math.Min(floorElements.Count, document.Floors.Count); index++)
        {
            XElement floorElement = floorElements[index];
            ElevateProjectEditorFloor floor = document.Floors[index];
            cumulativeFloorLevel += floor.InterfloorHeight;
            SetAttribute(floorElement, "FloorName", floor.FloorName);
            SetAttribute(floorElement, "FloorLevel", FormatDouble(cumulativeFloorLevel));
            SetAttribute(floorElement, "NoOfPeople", FormatDouble(floor.Population));
            SetAttribute(floorElement, "EntranceFloor", floor.EntranceFloor ? "True" : "False");
        }

        UpdateFloorReferences(root, originalFloorNames, document.Floors);

        standardPassenger.SetElementValue("Incoming", FormatDouble(document.Traffic.IncomingPercent));
        standardPassenger.SetElementValue("Outgoing", FormatDouble(document.Traffic.OutgoingPercent));
        standardPassenger.SetElementValue("Interfloor", FormatDouble(document.Traffic.InterfloorPercent));
        standardPassenger.SetElementValue("HandlingCapacity", FormatDouble(document.Traffic.HandlingCapacity));
        SetAttribute(standardPassenger, "LoadingTime", FormatDouble(document.Traffic.LoadingTimeSeconds));
        SetAttribute(standardPassenger, "UnloadingTime", FormatDouble(document.Traffic.UnloadingTimeSeconds));
        ApplyLiftSummary(standardPassenger, document.Cars);

        if (trafficPeriod is not null)
        {
            SetAttribute(trafficPeriod, "SplitUp", FormatDouble(document.Traffic.IncomingPercent));
            SetAttribute(trafficPeriod, "SplitDown", FormatDouble(document.Traffic.OutgoingPercent));
            SetAttribute(trafficPeriod, "SplitInterfloor", FormatDouble(document.Traffic.InterfloorPercent));
        }

        if (configuration is not null)
        {
            RebuildConfiguration(configuration, document.Cars, document.Floors);
        }
    }

    private static List<ElevateProjectEditorFloor> ParseFloors(XElement buildingData)
    {
        List<XElement> floorElements = buildingData.Elements("Floor").ToList();
        List<ElevateProjectEditorFloor> floors = [];
        double previousFloorLevel = 0d;

        for (int index = 0; index < floorElements.Count; index++)
        {
            XElement floorElement = floorElements[index];
            double floorLevel = ParseDouble((string?)floorElement.Attribute("FloorLevel"));
            floors.Add(new ElevateProjectEditorFloor
            {
                FloorIndex = index + 1,
                FloorName = (string?)floorElement.Attribute("FloorName") ?? string.Empty,
                InterfloorHeight = index == 0 ? floorLevel : floorLevel - previousFloorLevel,
                FloorLevel = floorLevel,
                Population = ParseDouble((string?)floorElement.Attribute("NoOfPeople")),
                EntranceFloor = ParseBool((string?)floorElement.Attribute("EntranceFloor")),
            });

            previousFloorLevel = floorLevel;
        }

        return floors;
    }

    private static List<ElevateProjectEditorCar> ParseCars(XElement? configuration)
    {
        if (configuration is null)
        {
            return [];
        }

        return configuration.Elements("Car")
            .Select(car =>
            {
                string capacity = (string?)car.Attribute("Capacity") ?? string.Empty;
                string floorArea = (string?)car.Attribute("FloorAreaM2") ?? string.Empty;
                CabinDimensions cabinDimensions = LiftRules.ResolveCabinDimensions(capacity, floorArea);
                string doorPreOpening = (string?)car.Attribute("DoorPreOpening") ?? string.Empty;
                string doorOpenTime = (string?)car.Attribute("DoorOpenTime") ?? string.Empty;
                string doorCloseTime = (string?)car.Attribute("DoorCloseTime") ?? string.Empty;
                bool resolvedDoorSpec = LiftRules.TryResolveDoorSpecification(
                    doorPreOpening,
                    doorOpenTime,
                    doorCloseTime,
                    out int doorWidthMm,
                    out DoorOpeningKind openingKind);

                return new ElevateProjectEditorCar
                {
                    Id = (string?)car.Attribute("Id") ?? string.Empty,
                    HomeShaft = (string?)car.Attribute("HomeShaft") ?? string.Empty,
                    CabinWidthMm = cabinDimensions.WidthMm,
                    CabinDepthMm = cabinDimensions.DepthMm,
                    CapacityKg = capacity,
                    FloorAreaM2 = floorArea,
                    Speed = (string?)car.Attribute("Speed") ?? string.Empty,
                    Acceleration = (string?)car.Attribute("Acceleration") ?? string.Empty,
                    Jerk = (string?)car.Attribute("Jerk") ?? string.Empty,
                    DoorPreOpening = doorPreOpening,
                    DoorWidthMm = resolvedDoorSpec ? doorWidthMm : 1000,
                    DoorOpeningKind = resolvedDoorSpec ? openingKind : DoorOpeningKind.Central,
                    DoorOpenTime = doorOpenTime,
                    DoorCloseTime = doorCloseTime,
                    HomeFloor = (string?)car.Attribute("HomeFloor") ?? string.Empty,
                    TemplateXml = car.ToString(SaveOptions.DisableFormatting),
                    ServedFloorIndexes = car.Elements("FloorServed")
                        .Select(floorServed => ParseInt((string?)floorServed.Attribute("FloorIndex")))
                        .Where(index => index > 0)
                        .Distinct()
                        .OrderBy(index => index)
                        .ToList(),
                };
            })
            .ToList();
    }

    private static void ApplyLiftSummary(XElement standardPassenger, IReadOnlyList<ElevateProjectEditorCar> cars)
    {
        ElevateProjectEditorCar? firstCar = cars.FirstOrDefault();
        if (firstCar is null)
        {
            return;
        }

        SetAttribute(standardPassenger, "NoOfElevatorsMode", "Specified");
        SetAttribute(standardPassenger, "NoOfLifts", cars.Count.ToString(CultureInfo.InvariantCulture));
        if (!CarsShareSummaryProfile(cars))
        {
            return;
        }

        SetAttribute(standardPassenger, "CapacityMode", "Specified");
        SetAttribute(standardPassenger, "Capacity", firstCar.CapacityKg);
        SetAttribute(standardPassenger, "CarAreaMode", "Specified");
        SetAttribute(standardPassenger, "CarAreaM2", firstCar.FloorAreaM2);
        SetAttribute(standardPassenger, "DoorMode", "Specified");
        SetAttribute(standardPassenger, "Pre-Open", firstCar.DoorPreOpening);
        SetAttribute(standardPassenger, "Open", firstCar.DoorOpenTime);
        SetAttribute(standardPassenger, "Close", firstCar.DoorCloseTime);
        SetAttribute(standardPassenger, "SpeedMode", "Specified");
        SetAttribute(standardPassenger, "Speed", firstCar.Speed);
        SetAttribute(standardPassenger, "AccelerationMode", "Specified");
        SetAttribute(standardPassenger, "Acceleration", firstCar.Acceleration);
        SetAttribute(standardPassenger, "JerkMode", "Specified");
        SetAttribute(standardPassenger, "Jerk", firstCar.Jerk);
        SetAttribute(standardPassenger, "HomeFloor", firstCar.HomeFloor);
    }

    private static void RebuildConfiguration(
        XElement configuration,
        IReadOnlyList<ElevateProjectEditorCar> cars,
        IReadOnlyList<ElevateProjectEditorFloor> floors)
    {
        XElement? templateCar = configuration.Elements("Car").FirstOrDefault();
        if (templateCar is null)
        {
            return;
        }

        List<XElement> existingCars = configuration.Elements("Car").ToList();
        foreach (XElement existingCar in existingCars)
        {
            existingCar.Remove();
        }

        SetAttribute(configuration, "NoOfLifts", cars.Count.ToString(CultureInfo.InvariantCulture));

        for (int index = 0; index < cars.Count; index++)
        {
            ElevateProjectEditorCar car = cars[index];
            XElement sourceTemplate = ResolveSourceCarElement(car, existingCars, templateCar, index);
            string doorsValue = (string?)sourceTemplate.Elements("FloorServed").FirstOrDefault()?.Attribute("Doors") ?? "Front Doors";
            XElement carElement = CreateCarElement(sourceTemplate, car, floors, doorsValue, index);

            SetAttribute(carElement, "Id", (index + 1).ToString(CultureInfo.InvariantCulture));
            SetAttribute(carElement, "Capacity", car.CapacityKg);
            SetAttribute(carElement, "FloorAreaM2", car.FloorAreaM2);
            SetAttribute(carElement, "Speed", car.Speed);
            SetAttribute(carElement, "Acceleration", car.Acceleration);
            SetAttribute(carElement, "Jerk", car.Jerk);
            SetAttribute(carElement, "DoorPreOpening", car.DoorPreOpening);
            SetAttribute(carElement, "DoorOpenTime", car.DoorOpenTime);
            SetAttribute(carElement, "DoorCloseTime", car.DoorCloseTime);
            SetAttribute(carElement, "HomeFloor", car.HomeFloor);
            SetAttribute(
                carElement,
                "HomeShaft",
                string.IsNullOrWhiteSpace(car.HomeShaft)
                    ? (index + 1).ToString(CultureInfo.InvariantCulture)
                    : car.HomeShaft);

            configuration.Add(carElement);
        }
    }

    private static bool CarsShareSummaryProfile(IReadOnlyList<ElevateProjectEditorCar> cars)
    {
        ElevateProjectEditorCar firstCar = cars[0];
        return cars.All(car =>
            string.Equals(car.CapacityKg, firstCar.CapacityKg, StringComparison.Ordinal) &&
            string.Equals(car.FloorAreaM2, firstCar.FloorAreaM2, StringComparison.Ordinal) &&
            string.Equals(car.Speed, firstCar.Speed, StringComparison.Ordinal) &&
            string.Equals(car.Acceleration, firstCar.Acceleration, StringComparison.Ordinal) &&
            string.Equals(car.Jerk, firstCar.Jerk, StringComparison.Ordinal) &&
            string.Equals(car.DoorPreOpening, firstCar.DoorPreOpening, StringComparison.Ordinal) &&
            string.Equals(car.DoorOpenTime, firstCar.DoorOpenTime, StringComparison.Ordinal) &&
            string.Equals(car.DoorCloseTime, firstCar.DoorCloseTime, StringComparison.Ordinal) &&
            string.Equals(car.HomeFloor, firstCar.HomeFloor, StringComparison.Ordinal));
    }

    private static XElement ResolveSourceCarElement(
        ElevateProjectEditorCar car,
        IReadOnlyList<XElement> existingCars,
        XElement fallbackTemplate,
        int index)
    {
        if (!string.IsNullOrWhiteSpace(car.TemplateXml))
        {
            try
            {
                return XElement.Parse(car.TemplateXml, LoadOptions.PreserveWhitespace);
            }
            catch
            {
                // Fall back to an existing template below.
            }
        }

        if (index < existingCars.Count)
        {
            return new XElement(existingCars[index]);
        }

        return new XElement(existingCars.LastOrDefault() ?? fallbackTemplate);
    }

    private static XElement CreateCarElement(
        XElement sourceTemplate,
        ElevateProjectEditorCar car,
        IReadOnlyList<ElevateProjectEditorFloor> floors,
        string doorsValue,
        int index)
    {
        XElement carElement = new(sourceTemplate);
        List<XElement> preservedChildren = carElement
            .Elements()
            .Where(element => element.Name != "FloorServed")
            .Select(element => new XElement(element))
            .ToList();

        carElement.Elements().Remove();

        List<int> servedFloorIndexes = car.ServedFloorIndexes.Count > 0
            ? car.ServedFloorIndexes
                .Where(floorIndex => floorIndex >= 1 && floorIndex <= floors.Count)
                .Distinct()
                .OrderBy(floorIndex => floorIndex)
                .ToList()
            : Enumerable.Range(1, floors.Count).ToList();

        if (servedFloorIndexes.Count == 0)
        {
            servedFloorIndexes = Enumerable.Range(1, floors.Count).ToList();
        }

        foreach (int floorIndex in servedFloorIndexes)
        {
            ElevateProjectEditorFloor floor = floors[floorIndex - 1];
            carElement.Add(
                new XElement(
                    "FloorServed",
                    new XAttribute("FloorName", floor.FloorName),
                    new XAttribute("FloorIndex", floorIndex),
                    new XAttribute("Doors", doorsValue)));
        }

        foreach (XElement preservedChild in preservedChildren)
        {
            carElement.Add(preservedChild);
        }

        SetAttribute(
            carElement,
            "Id",
            string.IsNullOrWhiteSpace(car.Id)
                ? (index + 1).ToString(CultureInfo.InvariantCulture)
                : car.Id);
        return carElement;
    }

    private static void UpdateFloorReferences(
        XElement root,
        IReadOnlyList<string> originalFloorNames,
        IReadOnlyList<ElevateProjectEditorFloor> floors)
    {
        for (int index = 0; index < Math.Min(originalFloorNames.Count, floors.Count); index++)
        {
            string newFloorName = floors[index].FloorName;
            string originalFloorName = originalFloorNames[index];

            foreach (XElement floorServed in root
                         .Descendants("FloorServed")
                         .Where(element => ParseInt((string?)element.Attribute("FloorIndex")) == index + 1))
            {
                SetAttribute(floorServed, "FloorName", newFloorName);
            }

            foreach (XElement series in root
                         .Descendants("Series")
                         .Where(element => string.Equals((string?)element.Attribute("Data"), originalFloorName, StringComparison.Ordinal)))
            {
                SetAttribute(series, "Data", newFloorName);
            }
        }
    }

    private static string ResolveBasePath(ElevateProjectEditorDocument document)
    {
        string? basePath = !string.IsNullOrWhiteSpace(document.SourcePath) && File.Exists(document.SourcePath)
            ? document.SourcePath
            : document.TemplatePath;

        if (string.IsNullOrWhiteSpace(basePath) || !File.Exists(basePath))
        {
            throw new InvalidOperationException("Cannot resolve base ELVX document for saving.");
        }

        return basePath;
    }

    private static XDocument LoadDocument(string path)
    {
        return XDocument.Load(path, LoadOptions.PreserveWhitespace);
    }

    private static string GetTemplatePath(BuildingType buildingType)
    {
        string? path = TryGetTemplatePath(buildingType);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Template not found for building type: " + buildingType);
        }

        return path;
    }

    private static string? TryGetTemplatePath(BuildingType buildingType)
    {
        string? repositoryRoot = FindRootContainingExamples();
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            return null;
        }

        string fileName = buildingType switch
        {
            BuildingType.Office => OfficeTemplateFileName,
            BuildingType.Residence => ResidenceTemplateFileName,
            BuildingType.Hotel => HotelTemplateFileName,
            _ => OfficeTemplateFileName,
        };

        string path = Path.Combine(repositoryRoot, ".example", fileName);
        return File.Exists(path) ? path : null;
    }

    private static string? FindRootContainingExamples()
    {
        foreach (string probeRoot in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            DirectoryInfo? current = new(probeRoot);
            while (current is not null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, ".example")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        return null;
    }

    private static BuildingType ParseBuildingType(string? rawValue)
    {
        return (rawValue ?? string.Empty).Trim() switch
        {
            "1" => BuildingType.Office,
            "2" => BuildingType.Hotel,
            "3" => BuildingType.Residence,
            string value when value.Equals("Office", StringComparison.OrdinalIgnoreCase) => BuildingType.Office,
            string value when value.Equals("Hotel", StringComparison.OrdinalIgnoreCase) => BuildingType.Hotel,
            string value when value.Equals("Residential", StringComparison.OrdinalIgnoreCase) => BuildingType.Residence,
            _ => BuildingType.Office,
        };
    }

    private static string ToBuildingTypeCode(BuildingType buildingType)
    {
        return buildingType switch
        {
            BuildingType.Office => "1",
            BuildingType.Hotel => "2",
            BuildingType.Residence => "3",
            _ => "1",
        };
    }

    private static int ParseInt(string? rawValue)
    {
        return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : 0;
    }

    private static double ParseDouble(string? primaryValue, string? fallbackValue = null)
    {
        foreach (string? candidate in new[] { primaryValue, fallbackValue })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (double.TryParse(candidate, NumberStyles.Float, CultureInfo.InvariantCulture, out double invariantValue))
            {
                return invariantValue;
            }

            if (double.TryParse(candidate, NumberStyles.Float, CultureInfo.GetCultureInfo("ru-RU"), out double russianValue))
            {
                return russianValue;
            }
        }

        return 0d;
    }

    private static bool ParseBool(string? rawValue)
    {
        return rawValue is not null &&
               rawValue.Equals("True", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatDouble(double value)
    {
        return value.ToString("0.000000", CultureInfo.InvariantCulture);
    }

    private static void SetAttribute(XElement element, string attributeName, string? value)
    {
        element.SetAttributeValue(attributeName, value ?? string.Empty);
    }

    private static string SanitizePathSegment(string value, string fallback)
    {
        string safeValue = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        foreach (char invalidChar in Path.GetInvalidFileNameChars())
        {
            safeValue = safeValue.Replace(invalidChar, '_');
        }

        return string.IsNullOrWhiteSpace(safeValue) ? fallback : safeValue;
    }
}
