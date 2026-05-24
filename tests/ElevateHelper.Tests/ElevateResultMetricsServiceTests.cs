using ElevateHelperWinUI.Models;
using ElevateHelperWinUI.Services;

namespace ElevateHelper.Tests;

public sealed class ElevateResultMetricsServiceTests
{
    [Fact]
    public void ReadLatestMetrics_ReadsLastBatchResultsRow()
    {
        using TestWorkspace workspace = new();
        File.WriteAllText(
            Path.Combine(workspace.Path, "batch_results.csv"),
            "file;folder;;;;;AWT;;;;ATTD\n" +
            "Project01.elvx;.;x;x;x;x;18,5;x;x;x;80\n" +
            "Project02.elvx;.;x;x;x;x;24.25;x;x;x;90\n");

        ElevateResultMetrics? metrics = new ElevateResultMetricsService().ReadLatestMetrics(workspace.Path);

        Assert.NotNull(metrics);
        Assert.Equal(2, metrics.HandlingCapacityFiveMinute);
        Assert.Equal(24.25, metrics.AverageWaitingTimeSeconds);
    }

    [Fact]
    public void Format_UsesCurrentCultureForAwt()
    {
        string text = ElevateResultMetricsService.Format(
            new ElevateResultMetrics(5, 23.4),
            System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));

        Assert.Equal("HC5: 5%   AWT: 23,4 s", text);
    }

    private sealed class TestWorkspace : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "ElevateHelperMetricsTests",
            Guid.NewGuid().ToString("N"));

        public TestWorkspace()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup for temporary test data.
            }
        }
    }
}
