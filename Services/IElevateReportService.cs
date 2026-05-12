using ElevateHelperWinUI.Models;

namespace ElevateHelperWinUI.Services;

public interface IElevateReportService
{
    Task<ProcessingResult> PrintReportAsync(
        string path,
        BuildingType buildingType,
        CancellationToken cancellationToken = default);
}
