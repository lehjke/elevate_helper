namespace ElevateHelperWinUI.Models;

public sealed record ElevateIntegrationInfo(
    bool IsDetected,
    string? ExecutablePath,
    string? ProductVersion,
    string DetectionSource,
    IReadOnlyList<string> ProbedPaths);
