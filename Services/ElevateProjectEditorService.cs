using System.Globalization;
using System.Xml.Linq;
using ElevateHelperWinUI.Models;

namespace ElevateHelperWinUI.Services;

public sealed class ElevateProjectEditorService : IElevateProjectEditorService
{
    private const string OfficeTemplateFileName = "Office.elvx";
    private const string ResidenceTemplateFileName = "Residential.elvx";
    private const string HotelTemplateFileName = "Hotel.elvx";
    private const string OfficeDispatcherAlgorithmName = "Mixed Control (Enhanced ACA)";
    private const string GroupCollectiveDispatcherAlgorithmName = "Group Collective";
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

        List<ElevateProjectEditorFloor> floors = ParseFloors(buildingData, standardPassenger);
        List<ElevateProjectEditorCar> cars = ParseCars(configuration);
        string logoFile = ResolveLogoFile(root, jobData);

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
                LogoFile = logoFile,
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
        BuildingType normalizedBuildingType = document.Building.BuildingType;
        string dispatcherAlgorithmName = ResolveDispatcherAlgorithmName(normalizedBuildingType);
        double absenteeismPercent = ResolveAbsenteeismPercent(normalizedBuildingType);
        List<ElevateProjectEditorCar> normalizedCars = document.Cars
            .Select(NormalizeCarRules)
            .ToList();
        document.BuildingType = normalizedBuildingType;
        document.Analysis.DispatcherAlgorithmName = dispatcherAlgorithmName;
        document.Building.AbsenteeismPercent = absenteeismPercent;
        document.Cars = normalizedCars;
        List<string> originalFloorNames = buildingData.Elements("Floor")
            .Select(floorElement => (string?)floorElement.Attribute("FloorName") ?? string.Empty)
            .ToList();

        SetAttribute(jobData, "JobTitle", document.Job.Title);
        SetAttribute(jobData, "JobNo", document.Job.Number);
        SetAttribute(jobData, "CalculationTitle", document.Job.CalculationTitle);
        SetAttribute(jobData, "MadeBy", document.Job.MadeBy);
        SetAttribute(jobData, "CheckedBy", document.Job.CheckedBy);
        SetAttribute(jobData, "Company", document.Job.Company);
        SetAttribute(jobData, "LogoFile", document.Job.LogoFile);
        SetLogoFileName(root, document.Job.LogoFile);

        SetAttribute(algorithm, "AlgorithmSource", "Standard");
        SetAttribute(algorithm, "AlgorithmName", dispatcherAlgorithmName);
        SetAttribute(algorithm, "Mode", document.Analysis.TrafficMode);
        if (simulationParameters is not null)
        {
            SetAttribute(simulationParameters, "NoOfSimulationsToRunForEachConfiguration", document.Analysis.SimulationsPerConfiguration.ToString(CultureInfo.InvariantCulture));
            SetAttribute(simulationParameters, "NoOfLearningRuns", document.Analysis.LearningRuns.ToString(CultureInfo.InvariantCulture));
            SetAttribute(simulationParameters, "RandomNumberSeedForPassengerGenerator", document.Analysis.RandomSeed.ToString(CultureInfo.InvariantCulture));
        }

        SetAttribute(buildingData, "BuildingType", ToBuildingTypeCode(normalizedBuildingType));
        SetAttribute(buildingData, "AbsenteeismPercent", FormatDouble(absenteeismPercent));
        SetAttribute(buildingData, "NoOfFloors", document.Floors.Count.ToString(CultureInfo.InvariantCulture));
        RebuildBuildingFloors(buildingData, document.Floors);
        RebuildStandardPassengerFloors(standardPassenger, originalFloorNames, document.Floors);
        RebuildXDispatchFloors(
            elevatorData.Element("XDispatch"),
            document.Floors,
            UsesDestinationCallStations(dispatcherAlgorithmName));
        RebuildPassengerDemand(passengerData.Element("Advanced"), document.Floors);
        UpdateFloorReferences(root, document.Floors);

        standardPassenger.SetElementValue("Incoming", FormatDouble(document.Traffic.IncomingPercent));
        standardPassenger.SetElementValue("Outgoing", FormatDouble(document.Traffic.OutgoingPercent));
        standardPassenger.SetElementValue("Interfloor", FormatDouble(document.Traffic.InterfloorPercent));
        standardPassenger.SetElementValue("HandlingCapacity", FormatDouble(document.Traffic.HandlingCapacity));
        SetAttribute(standardPassenger, "LoadingTime", FormatDouble(document.Traffic.LoadingTimeSeconds));
        SetAttribute(standardPassenger, "UnloadingTime", FormatDouble(document.Traffic.UnloadingTimeSeconds));
        ApplyLiftSummary(standardPassenger, normalizedCars);

        if (trafficPeriod is not null)
        {
            SetAttribute(trafficPeriod, "SplitUp", FormatDouble(document.Traffic.IncomingPercent));
            SetAttribute(trafficPeriod, "SplitDown", FormatDouble(document.Traffic.OutgoingPercent));
            SetAttribute(trafficPeriod, "SplitInterfloor", FormatDouble(document.Traffic.InterfloorPercent));
        }

        if (configuration is not null)
        {
            RebuildConfiguration(configuration, normalizedCars, document.Floors);
        }
    }

    private static ElevateProjectEditorCar NormalizeCarRules(ElevateProjectEditorCar car)
    {
        if (car.CabinWidthMm <= 0 || car.CabinDepthMm <= 0)
        {
            CabinDimensions cabinDimensions = LiftRules.ResolveCabinDimensions(car.CapacityKg, car.FloorAreaM2);
            if (car.CabinWidthMm <= 0)
            {
                car.CabinWidthMm = cabinDimensions.WidthMm;
            }

            if (car.CabinDepthMm <= 0)
            {
                car.CabinDepthMm = cabinDimensions.DepthMm;
            }
        }

        int doorWidthMm = car.DoorWidthMm > 0 ? car.DoorWidthMm : 1000;
        DoorProfile doorProfile = LiftRules.ResolveDoorProfile(doorWidthMm, car.DoorOpeningKind);
        MotionProfile motionProfile = LiftRules.ResolveMotionProfile(car.Speed);

        car.DoorWidthMm = doorWidthMm;
        car.FloorAreaM2 = FormatDouble(LiftRules.ResolveCarAreaSquareMeters(car.CabinWidthMm, car.CabinDepthMm));
        car.Acceleration = motionProfile.Acceleration;
        car.Jerk = motionProfile.Jerk;
        car.DoorPreOpening = doorProfile.DoorPreOpening;
        car.DoorOpenTime = doorProfile.DoorOpenTime;
        car.DoorCloseTime = doorProfile.DoorCloseTime;
        return car;
    }

    private static string ResolveDispatcherAlgorithmName(BuildingType buildingType)
    {
        return buildingType == BuildingType.Office
            ? OfficeDispatcherAlgorithmName
            : GroupCollectiveDispatcherAlgorithmName;
    }

    private static double ResolveAbsenteeismPercent(BuildingType buildingType)
    {
        return buildingType == BuildingType.Office ? 20d : 0d;
    }

    private static bool UsesDestinationCallStations(string dispatcherAlgorithmName)
    {
        return dispatcherAlgorithmName.Contains("Mixed Control", StringComparison.OrdinalIgnoreCase) ||
               dispatcherAlgorithmName.Contains("ACA", StringComparison.OrdinalIgnoreCase);
    }

    private static List<ElevateProjectEditorFloor> ParseFloors(XElement buildingData, XElement standardPassenger)
    {
        List<XElement> floorElements = buildingData.Elements("Floor").ToList();
        List<XElement> entranceBiasElements = standardPassenger.Elements("Floor").ToList();
        List<ElevateProjectEditorFloor> floors = [];
        double previousFloorLevel = 0d;

        for (int index = 0; index < floorElements.Count; index++)
        {
            XElement floorElement = floorElements[index];
            double floorLevel = ParseDouble((string?)floorElement.Attribute("FloorLevel"));
            floors.Add(new ElevateProjectEditorFloor
            {
                FloorIndex = index + 1,
                SourceFloorName = (string?)floorElement.Attribute("FloorName") ?? string.Empty,
                FloorName = (string?)floorElement.Attribute("FloorName") ?? string.Empty,
                InterfloorHeight = index == 0 ? floorLevel : floorLevel - previousFloorLevel,
                FloorLevel = floorLevel,
                Population = ParseDouble((string?)floorElement.Attribute("NoOfPeople")),
                EntranceFloor = ParseBool((string?)floorElement.Attribute("EntranceFloor")),
                EntranceBiasPercent = index < entranceBiasElements.Count
                    ? ParseDouble((string?)entranceBiasElements[index].Attribute("EntranceBias"))
                    : 0d,
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
                        .Where(ElevateReportService.IsFloorServedElement)
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

    private static void RebuildBuildingFloors(
        XElement buildingData,
        IReadOnlyList<ElevateProjectEditorFloor> floors)
    {
        List<XElement> existingFloors = buildingData.Elements("Floor").ToList();
        XElement? firstNonFloorElement = buildingData.Elements().FirstOrDefault(element => element.Name != "Floor");
        foreach (XElement existingFloor in existingFloors)
        {
            existingFloor.Remove();
        }

        double cumulativeFloorLevel = 0d;
        for (int index = 0; index < floors.Count; index++)
        {
            ElevateProjectEditorFloor floor = floors[index];
            cumulativeFloorLevel += floor.InterfloorHeight;
            XElement templateFloor = ResolveFloorTemplate(existingFloors, floor, index);
            XElement floorElement = new(templateFloor);
            SetAttribute(floorElement, "FloorName", floor.FloorName);
            SetAttribute(floorElement, "FloorLevel", FormatDouble(cumulativeFloorLevel));
            SetAttribute(floorElement, "NoOfPeople", FormatDouble(floor.Population));
            SetAttribute(floorElement, "EntranceFloor", floor.EntranceFloor ? "True" : "False");

            if (firstNonFloorElement is not null)
            {
                firstNonFloorElement.AddBeforeSelf(floorElement);
            }
            else
            {
                buildingData.Add(floorElement);
            }
        }
    }

    private static XElement ResolveFloorTemplate(
        IReadOnlyList<XElement> existingFloors,
        ElevateProjectEditorFloor floor,
        int index)
    {
        if (!string.IsNullOrWhiteSpace(floor.SourceFloorName))
        {
            XElement? matchedFloor = existingFloors.FirstOrDefault(element =>
                string.Equals((string?)element.Attribute("FloorName"), floor.SourceFloorName, StringComparison.Ordinal));
            if (matchedFloor is not null)
            {
                return matchedFloor;
            }
        }

        if (existingFloors.Count == 0)
        {
            return new XElement(
                "Floor",
                new XAttribute("FloorName", floor.FloorName),
                new XAttribute("FloorLevel", "0.000000"),
                new XAttribute("NoOfPeople", "0.000000"),
                new XAttribute("Area", "0.000000"),
                new XAttribute("AreaPerPerson", "-1.000000"),
                new XAttribute("EntranceFloor", "False"),
                new XAttribute("UserInterface", "0"));
        }

        return existingFloors[Math.Min(index, existingFloors.Count - 1)];
    }

    private static void RebuildStandardPassengerFloors(
        XElement standardPassenger,
        IReadOnlyList<string> originalFloorNames,
        IReadOnlyList<ElevateProjectEditorFloor> floors)
    {
        List<XElement> existingFloorElements = standardPassenger.Elements("Floor").ToList();
        Dictionary<string, XElement> floorBiasBySourceName = originalFloorNames
            .Select((name, index) => new { name, index })
            .Where(item => item.index < existingFloorElements.Count && !string.IsNullOrWhiteSpace(item.name))
            .ToDictionary(item => item.name, item => existingFloorElements[item.index], StringComparer.Ordinal);

        foreach (XElement existingFloorElement in existingFloorElements)
        {
            existingFloorElement.Remove();
        }

        foreach (ElevateProjectEditorFloor floor in floors)
        {
            XElement sourceElement = !string.IsNullOrWhiteSpace(floor.SourceFloorName) &&
                                     floorBiasBySourceName.TryGetValue(floor.SourceFloorName, out XElement? matchedBiasElement)
                ? matchedBiasElement
                : new XElement("Floor", new XAttribute("EntranceBias", "0.000000"));

            XElement floorElement = new(sourceElement);
            SetAttribute(floorElement, "EntranceBias", FormatDouble(floor.EntranceBiasPercent));
            standardPassenger.Add(floorElement);
        }
    }

    private static void RebuildXDispatchFloors(
        XElement? xDispatch,
        IReadOnlyList<ElevateProjectEditorFloor> floors,
        bool enableDestinationCallStations)
    {
        if (xDispatch is null)
        {
            return;
        }

        List<XElement> existingFloorElements = xDispatch.Elements("Floor").ToList();
        foreach (XElement existingFloorElement in existingFloorElements)
        {
            existingFloorElement.Remove();
        }

        for (int index = 0; index < floors.Count; index++)
        {
            ElevateProjectEditorFloor floor = floors[index];
            XElement template = ResolveFloorTemplate(existingFloorElements, floor, index);
            XElement floorElement = new(template);
            SetAttribute(floorElement, "FloorName", floor.FloorName);
            SetAttribute(floorElement, "DestinationButtons", enableDestinationCallStations ? "True" : "False");
            SetAttribute(floorElement, "UpCallsServedUpPeak", "True");
            SetAttribute(floorElement, "DownCallsServedUpPeak", "True");
            xDispatch.Add(floorElement);
        }
    }

    private static void RebuildPassengerDemand(
        XElement? advancedPassengerData,
        IReadOnlyList<ElevateProjectEditorFloor> floors)
    {
        if (advancedPassengerData is null)
        {
            return;
        }

        foreach (XElement period in advancedPassengerData.Elements("Period"))
        {
            XElement? passengerDemand = period.Element("PassengerDemand");
            if (passengerDemand is null)
            {
                continue;
            }

            Dictionary<string, XElement> existingFromByName = passengerDemand
                .Elements("From")
                .Where(element => !string.IsNullOrWhiteSpace((string?)element.Attribute("FloorName")))
                .GroupBy(element => (string?)element.Attribute("FloorName") ?? string.Empty, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            foreach (XElement existingFrom in passengerDemand.Elements("From").ToList())
            {
                existingFrom.Remove();
            }

            foreach (ElevateProjectEditorFloor fromFloor in floors)
            {
                XElement? sourceFrom = !string.IsNullOrWhiteSpace(fromFloor.SourceFloorName) &&
                                       existingFromByName.TryGetValue(fromFloor.SourceFloorName, out XElement? matchedFromElement)
                    ? matchedFromElement
                    : null;

                string arrivalRate = (string?)sourceFrom?.Attribute("ArrivalRate") ?? "0.000000";
                XElement fromElement = new(
                    "From",
                    new XAttribute("FloorName", fromFloor.FloorName),
                    new XAttribute("ArrivalRate", arrivalRate));

                Dictionary<string, string> destinationProbabilities = sourceFrom?
                    .Elements("To")
                    .Where(element => !string.IsNullOrWhiteSpace((string?)element.Attribute("FloorName")))
                    .GroupBy(element => (string?)element.Attribute("FloorName") ?? string.Empty, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => (string?)group.First().Attribute("DestinationProbability") ?? "0.000000",
                        StringComparer.Ordinal)
                    ?? new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (ElevateProjectEditorFloor destinationFloor in floors)
                {
                    string probability = !string.IsNullOrWhiteSpace(destinationFloor.SourceFloorName) &&
                                         destinationProbabilities.TryGetValue(destinationFloor.SourceFloorName, out string? existingProbability)
                        ? existingProbability
                        : "0.000000";

                    if (string.Equals(fromFloor.FloorName, destinationFloor.FloorName, StringComparison.Ordinal))
                    {
                        probability = "0.000000";
                    }

                    fromElement.Add(
                        new XElement(
                            "To",
                            new XAttribute("FloorName", destinationFloor.FloorName),
                            new XAttribute("DestinationProbability", probability)));
                }

                passengerDemand.Add(fromElement);
            }
        }
    }

    private static void UpdateFloorReferences(
        XElement root,
        IReadOnlyList<ElevateProjectEditorFloor> floors)
    {
        foreach (ElevateProjectEditorFloor floor in floors)
        {
            if (string.IsNullOrWhiteSpace(floor.SourceFloorName) ||
                string.Equals(floor.SourceFloorName, floor.FloorName, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (XAttribute attribute in root
                         .Descendants()
                         .Attributes("FloorName")
                         .Where(attribute => string.Equals(attribute.Value, floor.SourceFloorName, StringComparison.Ordinal)))
            {
                attribute.Value = floor.FloorName;
            }

            foreach (XAttribute attribute in root
                         .Descendants()
                         .Attributes("Data")
                         .Where(attribute => string.Equals(attribute.Value, floor.SourceFloorName, StringComparison.Ordinal)))
            {
                attribute.Value = floor.FloorName;
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

    private static string ResolveLogoFile(XElement root, XElement jobData)
    {
        string? jobLogoFile = (string?)jobData.Attribute("LogoFile");
        if (!string.IsNullOrWhiteSpace(jobLogoFile))
        {
            return jobLogoFile;
        }

        return root.Element("LogoFileName")?.Value ?? string.Empty;
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

    private static void SetLogoFileName(XElement root, string? logoFile)
    {
        XElement? logoFileNameElement = root.Element("LogoFileName");
        if (logoFileNameElement is not null)
        {
            logoFileNameElement.Value = logoFile ?? string.Empty;
            return;
        }

        if (string.IsNullOrWhiteSpace(logoFile))
        {
            return;
        }

        XElement newLogoFileNameElement = new("LogoFileName", logoFile);
        XElement? simulatorMenuItem = root.Element("LiftSimulatorServerMenuItem");
        if (simulatorMenuItem is not null)
        {
            simulatorMenuItem.AddBeforeSelf(newLogoFileNameElement);
            return;
        }

        root.Add(newLogoFileNameElement);
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
