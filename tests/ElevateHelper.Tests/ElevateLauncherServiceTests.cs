using System.Reflection;
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
    [InlineData("Elevate - [Design 1]", true)]
    [InlineData("Probe 01", false)]
    public void IsDesignWindowTitle_RecognizesDesignWindow(string title, bool expected)
    {
        bool actual = ElevateLauncherService.IsDesignWindowTitle(title);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Do you want to save changes?", true)]
    [InlineData("Сохранить изменения перед закрытием?", true)]
    [InlineData("Сохранить изменения в Design1?", true)]
    [InlineData("Сохранить изменения в Design 1?", true)]
    [InlineData("Close Elevate without saving?", true)]
    [InlineData("Закрыть Design 1?", true)]
    [InlineData("Elevate has finished processing.", false)]
    [InlineData("The selected folder does not exist.", false)]
    public void IsSavePromptText_MatchesOnlySavePrompts(string text, bool expected)
    {
        bool actual = ElevateLauncherService.IsSavePromptText(text);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("No", true)]
    [InlineData("&No", true)]
    [InlineData("Нет", true)]
    [InlineData("&Нет", true)]
    [InlineData("Да", false)]
    [InlineData("Отмена", false)]
    public void IsNoSaveButtonText_RecognizesLocalizedNoButton(string text, bool expected)
    {
        bool actual = ElevateLauncherService.IsNoSaveButtonText(text);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IsSaveConfirmationDialogText_RecognizesRussianElevateDesignPrompt()
    {
        bool actual = ElevateLauncherService.IsSaveConfirmationDialogText(
            "Elevate",
            new[]
            {
                "Сохранить изменения в Design1?",
                "Да",
                "Нет",
                "Отмена",
            });

        Assert.True(actual);
    }

    [Fact]
    public void IsSaveConfirmationDialogText_RecognizesRussianElevateDesignPromptWithSpace()
    {
        bool actual = ElevateLauncherService.IsSaveConfirmationDialogText(
            "Elevate",
            new[]
            {
                "Сохранить изменения в Design 1?",
                "Да",
                "Нет",
                "Отмена",
            });

        Assert.True(actual);
    }

    [Theory]
    [InlineData("Elevate", "Could not open results file. Try again?", true)]
    [InlineData("Elevate", "COULD NOT OPEN RESULTS FILE", true)]
    [InlineData("Elevate", "Could not open project file. Try again?", false)]
    [InlineData("Elevate", "Сохранить изменения в Design 1?", false)]
    public void IsResultFileOpenErrorDialogText_MatchesOnlyResultOpenFailure(
        string title,
        string message,
        bool expected)
    {
        bool actual = ElevateLauncherService.IsResultFileOpenErrorDialogText(title, [message]);

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

    [Fact]
    public void GetProjectPrefix_KeepsJobNumberWhenFileNameHasTrailingCopyIndex()
    {
        string prefix = InvokeGetProjectPrefix(
            Enumerable
                .Range(1, 13)
                .Select(index => $"project g1 R07 {index:00}.elvx")
                .ToArray());

        bool parsed = ElevateLauncherService.TryParseWindowNumber(
            "Elevate - [project g1 R07 01.elvx]",
            prefix,
            out int number);

        Assert.Equal("project g1 R07", prefix);
        Assert.True(parsed);
        Assert.Equal(1, number);
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
    public void CountCompletedResultFiles_ResumeBaselineIncludesExistingOutputs()
    {
        using LauncherTestWorkspace workspace = new();
        string existingCsvPath = Path.Combine(workspace.RootPath, "Probe 01_elvx.csv");
        File.WriteAllText(existingCsvPath, "done");
        ElevateLauncherService.ResultFileBaseline resumeBaseline = new(
            DateTimeOffset.UtcNow,
            new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase));

        int actual = ElevateLauncherService.CountCompletedResultFiles(
            workspace.RootPath,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Probe 01" },
            resumeBaseline);

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

    [Fact]
    public void HasCompletedScenarioOutputs_ReturnsTrueWhenBatchAndAllCsvOutputsExist()
    {
        using LauncherTestWorkspace workspace = new();
        File.WriteAllText(Path.Combine(workspace.RootPath, "Probe 01.elvx"), "<Project />");
        File.WriteAllText(Path.Combine(workspace.RootPath, "Probe 02.elvx"), "<Project />");
        File.WriteAllText(Path.Combine(workspace.RootPath, "Probe 01_elvx.csv"), "done");
        File.WriteAllText(Path.Combine(workspace.RootPath, "Probe 02_elvx.csv"), "done");
        File.WriteAllText(Path.Combine(workspace.RootPath, "batch_results.csv"), "done");

        bool actual = ElevateLauncherService.HasCompletedScenarioOutputs(workspace.RootPath);

        Assert.True(actual);
    }

    [Fact]
    public void HasCompletedScenarioOutputs_ReturnsFalseWhenAnyCsvOutputIsMissing()
    {
        using LauncherTestWorkspace workspace = new();
        File.WriteAllText(Path.Combine(workspace.RootPath, "Probe 01.elvx"), "<Project />");
        File.WriteAllText(Path.Combine(workspace.RootPath, "Probe 02.elvx"), "<Project />");
        File.WriteAllText(Path.Combine(workspace.RootPath, "Probe 01_elvx.csv"), "done");
        File.WriteAllText(Path.Combine(workspace.RootPath, "batch_results.csv"), "done");

        bool actual = ElevateLauncherService.HasCompletedScenarioOutputs(workspace.RootPath);

        Assert.False(actual);
    }

    [Fact]
    public void HasCompletedScenarioOutputs_ReturnsFalseWhenExpectedCopyCountDiffers()
    {
        using LauncherTestWorkspace workspace = new();
        File.WriteAllText(Path.Combine(workspace.RootPath, "Probe 01.elvx"), "<Project />");
        File.WriteAllText(Path.Combine(workspace.RootPath, "Probe 01_elvx.csv"), "done");
        File.WriteAllText(Path.Combine(workspace.RootPath, "batch_results.csv"), "done");

        bool actual = ElevateLauncherService.HasCompletedScenarioOutputs(workspace.RootPath, expectedTotal: 2);

        Assert.False(actual);
    }

    [Theory]
    [InlineData("Probe 01_elvx.csv")]
    [InlineData("batch_results.csv")]
    public void HasCompletedScenarioOutputs_ReturnsFalseForEmptyRequiredOutput(string emptyFileName)
    {
        using LauncherTestWorkspace workspace = new();
        File.WriteAllText(Path.Combine(workspace.RootPath, "Probe 01.elvx"), "<Project />");
        File.WriteAllText(Path.Combine(workspace.RootPath, "Probe 01_elvx.csv"), "done");
        File.WriteAllText(Path.Combine(workspace.RootPath, "batch_results.csv"), "done");
        File.WriteAllText(Path.Combine(workspace.RootPath, emptyFileName), string.Empty);

        bool actual = ElevateLauncherService.HasCompletedScenarioOutputs(workspace.RootPath);

        Assert.False(actual);
    }

    [Fact]
    public void ProgressStallWatchdog_ResetsOnlyWhenObservableProgressChanges()
    {
        TimeSpan timeout = TimeSpan.FromMinutes(5);
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        ElevateLauncherService.ProgressStallWatchdog watchdog = new(timeout);
        ElevateLauncherService.ProgressActivity initial = new(1, 0, 0, false);

        watchdog.Observe(initial, startedAt);

        Assert.False(watchdog.IsStalled(startedAt + timeout - TimeSpan.FromSeconds(1)));
        Assert.True(watchdog.IsStalled(startedAt + timeout));

        watchdog.Observe(initial with { CompletedCsvFiles = 1 }, startedAt + timeout);

        Assert.False(watchdog.IsStalled(startedAt + timeout + TimeSpan.FromMinutes(4)));
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

    private static string InvokeGetProjectPrefix(IReadOnlyList<string> elvxFiles)
    {
        MethodInfo method = typeof(ElevateLauncherService).GetMethod(
            "GetProjectPrefix",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        return (string)method.Invoke(null, [elvxFiles])!;
    }
}
