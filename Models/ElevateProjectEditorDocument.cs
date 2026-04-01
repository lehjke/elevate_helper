using ElevateHelperWinUI.Services;

namespace ElevateHelperWinUI.Models;

public sealed class ElevateProjectEditorDocument
{
    public string? SourcePath { get; set; }

    public string? TemplatePath { get; set; }

    public BuildingType BuildingType { get; set; }

    public ElevateProjectEditorJobSection Job { get; set; } = new();

    public ElevateProjectEditorAnalysisSection Analysis { get; set; } = new();

    public ElevateProjectEditorBuildingSection Building { get; set; } = new();

    public ElevateProjectEditorTrafficSection Traffic { get; set; } = new();

    public List<ElevateProjectEditorFloor> Floors { get; set; } = [];

    public List<ElevateProjectEditorCar> Cars { get; set; } = [];
}

public sealed class ElevateProjectEditorJobSection
{
    public string Title { get; set; } = string.Empty;

    public string Number { get; set; } = string.Empty;

    public string CalculationTitle { get; set; } = string.Empty;

    public string MadeBy { get; set; } = string.Empty;

    public string CheckedBy { get; set; } = string.Empty;

    public string Company { get; set; } = string.Empty;

    public string LogoFile { get; set; } = string.Empty;
}

public sealed class ElevateProjectEditorAnalysisSection
{
    public string DispatcherAlgorithmName { get; set; } = string.Empty;

    public string TrafficMode { get; set; } = string.Empty;

    public int SimulationsPerConfiguration { get; set; }

    public int LearningRuns { get; set; }

    public int RandomSeed { get; set; }
}

public sealed class ElevateProjectEditorBuildingSection
{
    public BuildingType BuildingType { get; set; }

    public double AbsenteeismPercent { get; set; }

    public int NumberOfFloors { get; set; }
}

public sealed class ElevateProjectEditorTrafficSection
{
    public double IncomingPercent { get; set; }

    public double OutgoingPercent { get; set; }

    public double InterfloorPercent { get; set; }

    public double HandlingCapacity { get; set; }

    public double LoadingTimeSeconds { get; set; }

    public double UnloadingTimeSeconds { get; set; }
}

public sealed class ElevateProjectEditorFloor
{
    public int FloorIndex { get; set; }

    public string SourceFloorName { get; set; } = string.Empty;

    public string FloorName { get; set; } = string.Empty;

    public double InterfloorHeight { get; set; }

    public double FloorLevel { get; set; }

    public double Population { get; set; }

    public bool EntranceFloor { get; set; }
}

public sealed class ElevateProjectEditorCar
{
    public string Id { get; set; } = string.Empty;

    public string HomeShaft { get; set; } = string.Empty;

    public int CabinWidthMm { get; set; }

    public int CabinDepthMm { get; set; }

    public string CapacityKg { get; set; } = string.Empty;

    public string FloorAreaM2 { get; set; } = string.Empty;

    public string Speed { get; set; } = string.Empty;

    public string Acceleration { get; set; } = string.Empty;

    public string Jerk { get; set; } = string.Empty;

    public string DoorPreOpening { get; set; } = string.Empty;

    public int DoorWidthMm { get; set; }

    public DoorOpeningKind DoorOpeningKind { get; set; }

    public string DoorOpenTime { get; set; } = string.Empty;

    public string DoorCloseTime { get; set; } = string.Empty;

    public string HomeFloor { get; set; } = string.Empty;

    public string TemplateXml { get; set; } = string.Empty;

    public List<int> ServedFloorIndexes { get; set; } = [];
}
