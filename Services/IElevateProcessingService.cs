using ElevateHelperWinUI.Models;

namespace ElevateHelperWinUI.Services;

public interface IElevateProcessingService
{
    int GetDefaultCopies(BuildingType buildingType);

    Task<ProcessingResult> RunAsync(
        string path,
        BuildingType buildingType,
        bool includeLunchPeak,
        CancellationToken cancellationToken = default);

    Task<ProcessingResult> RunAsync(
        int copiesCount,
        string path,
        BuildingType buildingType,
        bool includeLunchPeak,
        CancellationToken cancellationToken = default);
}
