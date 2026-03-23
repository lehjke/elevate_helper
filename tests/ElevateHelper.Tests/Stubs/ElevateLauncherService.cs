namespace ElevateHelperWinUI.Services;

// Test-only stub to satisfy ElevateProcessingService default constructor dependency.
public sealed class ElevateLauncherService : IElevateLauncherService
{
    public Task LaunchResidenceAsync(string path, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Test stub. Use ElevateProcessingService(IElevateLauncherService) overload.");
    }

    public Task LaunchOfficeAsync(string path, bool includeLunchPeak, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Test stub. Use ElevateProcessingService(IElevateLauncherService) overload.");
    }
}
