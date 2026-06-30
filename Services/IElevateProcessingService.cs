using ElevateHelperWinUI.Models;

namespace ElevateHelperWinUI.Services;

public interface IElevateProcessingService
{
    int GetDefaultCopies(BuildingType buildingType);

    IReadOnlyList<ElevateRunManifest> GetRunHistory(string path);

    Task<ProcessingResult> RunAsync(
        string path,
        BuildingType buildingType,
        bool includeLunchPeak,
        CancellationToken cancellationToken = default);

    Task<ProcessingResult> RunAsync(
        string path,
        BuildingType buildingType,
        bool includeLunchPeak,
        IProgress<ElevateProgressInfo>? progress,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(path, buildingType, includeLunchPeak, cancellationToken);
    }

    Task<ProcessingResult> RunAsync(
        string path,
        BuildingType buildingType,
        bool includeLunchPeak,
        IProgress<ElevateProgressInfo>? morningProgress,
        IProgress<ElevateProgressInfo>? lunchProgress,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(path, buildingType, includeLunchPeak, cancellationToken);
    }

    Task<ProcessingResult> RunAsync(
        int copiesCount,
        string path,
        BuildingType buildingType,
        bool includeLunchPeak,
        CancellationToken cancellationToken = default);

    Task<ProcessingResult> RunAsync(
        int copiesCount,
        string path,
        BuildingType buildingType,
        bool includeLunchPeak,
        IProgress<ElevateProgressInfo>? progress,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(copiesCount, path, buildingType, includeLunchPeak, cancellationToken);
    }

    Task<ProcessingResult> RunAsync(
        int copiesCount,
        string path,
        BuildingType buildingType,
        bool includeLunchPeak,
        IProgress<ElevateProgressInfo>? morningProgress,
        IProgress<ElevateProgressInfo>? lunchProgress,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(copiesCount, path, buildingType, includeLunchPeak, cancellationToken);
    }

    Task<ProcessingResult> RetryLastFailedRunAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task<ProcessingResult> RetryLastFailedRunAsync(
        string path,
        IProgress<ElevateProgressInfo>? progress,
        CancellationToken cancellationToken = default)
    {
        return RetryLastFailedRunAsync(path, cancellationToken);
    }

    Task<ProcessingResult> RetryLastFailedRunAsync(
        string path,
        IProgress<ElevateProgressInfo>? morningProgress,
        IProgress<ElevateProgressInfo>? lunchProgress,
        CancellationToken cancellationToken = default)
    {
        return RetryLastFailedRunAsync(path, cancellationToken);
    }

    Task<ProcessingResult> RunExistingBatchAsync(
        string path,
        BuildingType buildingType,
        bool includeLunchPeak,
        IProgress<ElevateProgressInfo>? morningProgress,
        IProgress<ElevateProgressInfo>? lunchProgress,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(path, buildingType, includeLunchPeak, morningProgress, lunchProgress, cancellationToken);
    }

    Task<ProcessingResult> RunExistingScenarioAsync(
        string scenarioPath,
        IProgress<ElevateProgressInfo>? progress,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            scenarioPath,
            BuildingType.Office,
            includeLunchPeak: false,
            progress,
            cancellationToken);
    }
}
