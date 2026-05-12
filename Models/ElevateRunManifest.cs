using System.Text.Json.Serialization;

namespace ElevateHelperWinUI.Models;

public sealed class ElevateRunManifest
{
    public int SchemaVersion { get; set; } = 1;

    public string RunId { get; set; } = string.Empty;

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string Status { get; set; } = ElevateRunManifestStatus.Running;

    public string WorkingFolder { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BuildingType BuildingType { get; set; }

    public bool IncludeLunchPeak { get; set; }

    public int CopiesCount { get; set; }

    public List<ElevateRunManifestStep> Steps { get; set; } = [];

    public List<ElevateRunManifestArtifact> Artifacts { get; set; } = [];

    public string? ErrorMessage { get; set; }
}

public sealed class ElevateRunManifestStep
{
    public string Name { get; set; } = string.Empty;

    public string Status { get; set; } = ElevateRunManifestStatus.Pending;

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? ErrorMessage { get; set; }
}

public sealed class ElevateRunManifestArtifact
{
    public string Kind { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string? Scenario { get; set; }
}

public static class ElevateRunManifestStatus
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public static class ElevateRunManifestStepNames
{
    public const string ValidateInputs = "Validate inputs";
    public const string PrepareAndRunElevate = "Prepare scenarios and run Elevate";
    public const string CollectArtifacts = "Collect artifacts";
}

public static class ElevateRunManifestArtifactKinds
{
    public const string ElevateProject = "elevate-project";
    public const string ElevateResult = "elevate-result";
    public const string BatchResults = "batch-results";
    public const string FloorArea = "floor-area";
    public const string CsvOutput = "csv-output";
}
