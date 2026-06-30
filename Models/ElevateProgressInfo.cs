namespace ElevateHelperWinUI.Models;

public sealed record ElevateProgressInfo(
    string ProjectPrefix,
    string Scenario,
    int Completed,
    int Total,
    string Source,
    string? WindowTitle = null,
    bool IsFinal = false,
    string? ErrorMessage = null)
{
    public double Percentage => Total <= 0
        ? 0
        : Math.Min(100d, Completed * 100d / Total);
}
