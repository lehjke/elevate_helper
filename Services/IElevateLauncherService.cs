using ElevateHelperWinUI.Models;

namespace ElevateHelperWinUI.Services;

public interface IElevateLauncherService
{
    void SetWindowsHidden(bool hidden)
    {
    }

    Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    Task LaunchResidenceAsync(string path, CancellationToken cancellationToken = default);

    Task LaunchResidenceAsync(
        string path,
        IProgress<ElevateProgressInfo>? progress,
        CancellationToken cancellationToken = default)
    {
        return LaunchResidenceAsync(path, cancellationToken);
    }

    Task LaunchExistingResidenceAsync(
        string path,
        IProgress<ElevateProgressInfo>? progress,
        CancellationToken cancellationToken = default)
    {
        return LaunchResidenceAsync(path, progress, cancellationToken);
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

    Task LaunchExistingOfficeAsync(
        string path,
        bool includeLunchPeak,
        IProgress<ElevateProgressInfo>? morningProgress,
        IProgress<ElevateProgressInfo>? lunchProgress,
        CancellationToken cancellationToken = default)
    {
        return LaunchOfficeAsync(path, includeLunchPeak, morningProgress, lunchProgress, cancellationToken);
    }
}
