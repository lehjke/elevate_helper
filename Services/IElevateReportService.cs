using ElevateHelperWinUI.Models;

namespace ElevateHelperWinUI.Services;

public interface IElevateReportService
{
    Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    Task<ProcessingResult> PrintReportAsync(
        string path,
        BuildingType buildingType,
        CancellationToken cancellationToken = default);

    Task<ProcessingResult> PrintReportAsync(
        string path,
        BuildingType buildingType,
        string? outputFolder,
        CancellationToken cancellationToken = default)
    {
        return PrintReportAsync(path, buildingType, cancellationToken);
    }
}
