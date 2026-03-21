using ElevateHelperWinUI.Models;

namespace ElevateHelperWinUI.Services;

public interface IElevateReportService
{
    Task<ProcessingResult> PrintReportAsync(string path, CancellationToken cancellationToken = default);
}
