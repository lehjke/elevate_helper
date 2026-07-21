namespace ElevateHelperWinUI.Models;

internal sealed record PersistedJobSnapshot(
    string Path,
    BuildingType BuildingType,
    bool IncludeLunchPeak,
    string? Title,
    string? ReportOutputRoot,
    DateTimeOffset SavedAtUtc);
