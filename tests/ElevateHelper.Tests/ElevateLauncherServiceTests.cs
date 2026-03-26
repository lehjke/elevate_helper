using ElevateHelperWinUI.Services;

namespace ElevateHelper.Tests;

public sealed class ElevateLauncherServiceTests
{
    [Theory]
    [InlineData("Elevate - [Probe 02.elvx]", true, "Probe 02.elvx")]
    [InlineData("Probe 02.elvx", false, "")]
    [InlineData("", false, "")]
    public void TryExtractDocumentTitle_ParsesBracketedTitles(string title, bool expectedResult, string expectedDocumentTitle)
    {
        bool result = ElevateLauncherService.TryExtractDocumentTitle(title, out string documentTitle);

        Assert.Equal(expectedResult, result);
        Assert.Equal(expectedDocumentTitle, documentTitle);
    }

    [Theory]
    [InlineData("Elevate - [Probe 02.elvx]", "Probe 02")]
    [InlineData(" Probe 02.elvx ", "Probe 02")]
    [InlineData("Design 1", "Design 1")]
    public void NormalizeWindowTitle_NormalizesElevateDocumentTitles(string title, string expected)
    {
        string actual = ElevateLauncherService.NormalizeWindowTitle(title);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Design 1", true)]
    [InlineData("Design1", true)]
    [InlineData("Elevate - [Design1]", true)]
    [InlineData("Probe 01", false)]
    public void IsDesignWindowTitle_RecognizesDesignWindow(string title, bool expected)
    {
        bool actual = ElevateLauncherService.IsDesignWindowTitle(title);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Probe 02", "Probe", true, 2)]
    [InlineData("Elevate - [Probe 09.elvx]", "Probe", true, 9)]
    [InlineData("Design 1", "Probe", false, 0)]
    [InlineData("Probe A", "Probe", false, 0)]
    public void TryParseWindowNumber_ExtractsProjectSequence(
        string title,
        string projectPrefix,
        bool expectedResult,
        int expectedNumber)
    {
        bool actual = ElevateLauncherService.TryParseWindowNumber(title, projectPrefix, out int number);

        Assert.Equal(expectedResult, actual);
        Assert.Equal(expectedNumber, number);
    }

    [Theory]
    [InlineData("Probe 01", true)]
    [InlineData("Elevate - [Probe 01.elvx]", true)]
    [InlineData("Design1", false)]
    [InlineData("Other 01", false)]
    public void IsProjectWindowTitle_MatchesOnlyProjectDocuments(string title, bool expected)
    {
        bool actual = ElevateLauncherService.IsProjectWindowTitle(title, "Probe");

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Probe 01_elvx", "Probe 01")]
    [InlineData("Probe 01", "Probe 01")]
    [InlineData("", "")]
    public void NormalizeResultBaseName_RemovesElvxSuffix(string baseName, string expected)
    {
        string actual = ElevateLauncherService.NormalizeResultBaseName(baseName);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CountCompletedResultFiles_CountsUniqueProjectOutputs()
    {
        using LauncherTestWorkspace workspace = new();
        File.WriteAllText(Path.Combine(workspace.RootPath, "Probe 01_elvx.csv"), "done");
        File.WriteAllText(Path.Combine(workspace.RootPath, "Probe 02.elvr"), "done");
        File.WriteAllText(Path.Combine(workspace.RootPath, "Probe 02_elvx.csv"), "duplicate");
        File.WriteAllText(Path.Combine(workspace.RootPath, "batch_results.csv"), "ignore");
        File.WriteAllText(Path.Combine(workspace.RootPath, "Other 01_elvx.csv"), "ignore");

        int actual = ElevateLauncherService.CountCompletedResultFiles(
            workspace.RootPath,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Probe 01",
                "Probe 02",
            });

        Assert.Equal(2, actual);
    }

    [Fact]
    public void CountCompletedResultFiles_IgnoresStaleOutputsFromPreviousRun()
    {
        using LauncherTestWorkspace workspace = new();
        string staleCsvPath = Path.Combine(workspace.RootPath, "Probe 01_elvx.csv");
        string freshResultPath = Path.Combine(workspace.RootPath, "Probe 02.elvr");

        File.WriteAllText(staleCsvPath, "old");
        DateTime staleWriteTimeUtc = File.GetLastWriteTimeUtc(staleCsvPath);

        ElevateLauncherService.ResultFileBaseline baseline = new(
            DateTimeOffset.UtcNow,
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase)
            {
                [staleCsvPath] = staleWriteTimeUtc,
            });

        File.WriteAllText(freshResultPath, "new");

        int actual = ElevateLauncherService.CountCompletedResultFiles(
            workspace.RootPath,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Probe 01",
                "Probe 02",
            },
            baseline);

        Assert.Equal(1, actual);
    }

    [Fact]
    public void CountCompletedCsvFiles_IgnoresElvrUntilCsvExists()
    {
        using LauncherTestWorkspace workspace = new();
        File.WriteAllText(Path.Combine(workspace.RootPath, "Probe 01.elvr"), "done");
        File.WriteAllText(Path.Combine(workspace.RootPath, "Probe 02_elvx.csv"), "done");

        int actual = ElevateLauncherService.CountCompletedCsvFiles(
            workspace.RootPath,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Probe 01",
                "Probe 02",
            },
            baseline: null);

        Assert.Equal(1, actual);
    }

    private sealed class LauncherTestWorkspace : IDisposable
    {
        public string RootPath { get; } = Path.Combine(
            Path.GetTempPath(),
            "ElevateHelperLauncherTests",
            Guid.NewGuid().ToString("N"));

        public LauncherTestWorkspace()
        {
            Directory.CreateDirectory(RootPath);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath))
                {
                    Directory.Delete(RootPath, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}
