using ElevateHelperWinUI.Models;

namespace ElevateHelperWinUI.Services;

public interface IElevateLauncherService
{
    Task LaunchResidenceAsync(string path, CancellationToken cancellationToken = default);

    Task LaunchResidenceAsync(
        string path,
        IProgress<ElevateProgressInfo>? progress,
        CancellationToken cancellationToken = default)
    {
        return LaunchResidenceAsync(path, cancellationToken);
    }

    Task LaunchOfficeAsync(
        string path,
        bool includeLunchPeak,
        CancellationToken cancellationToken = default);

    Task LaunchOfficeAsync(
        string path,
        bool includeLunchPeak,
        IProgress<ElevateProgressInfo>? morningProgress,
        IProgress<ElevateProgressInfo>? lunchProgress,
        CancellationToken cancellationToken = default)
    {
        return LaunchOfficeAsync(path, includeLunchPeak, cancellationToken);
    }
}
