namespace ElevateHelperWinUI.Services;

public interface IElevateLauncherService
{
    Task LaunchResidenceAsync(string path, CancellationToken cancellationToken = default);

    Task LaunchOfficeAsync(
        string path,
        bool includeLunchPeak,
        CancellationToken cancellationToken = default);
}
