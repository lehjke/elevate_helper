using ElevateHelperWinUI.Services;

namespace ElevateHelper.Tests;

public sealed class ElevateReportServiceTests
{
    [Fact]
    public void BuildStepFileName_MatchesVbaNaming()
    {
        string stepFileName = ElevateReportService.BuildStepFileName("Luzhniki 24 B R001.csv", 14);

        Assert.Equal("Luzhniki 24 B R 14.csv", stepFileName);
    }

    [Fact]
    public void ResolveExistingCsvPath_FindsStepFileUnderReportRoot_WhenBatchFolderIsWrong()
    {
        using ReportTestWorkspace workspace = new();
        string reportRoot = workspace.CreateDirectory("morning");
        string nestedWrongFolder = workspace.CreateDirectory(Path.Combine("morning", "Project", "morning"));
        string stepFilePath = Path.Combine(reportRoot, "Project R 14.csv");
        File.WriteAllText(stepFilePath, "test");

        string? resolvedPath = ElevateReportService.ResolveExistingCsvPath(
            "Project R 14.csv",
            reportRoot,
            nestedWrongFolder);

        Assert.Equal(stepFilePath, resolvedPath);
    }

    [Fact]
    public void BuildOutputPaths_ReturnsExcelAndPdfPaths()
    {
        string outputFolder = Path.Combine("C:\\", "Reports");
        ElevateReportService.GeneratedReportPaths outputPaths =
            ElevateReportService.BuildOutputPaths(outputFolder, "Project: 1", "Tower/A");

        Assert.Equal(Path.Combine(outputFolder, "Project_ 1 Tower_A.xlsx"), outputPaths.ExcelPath);
        Assert.Equal(Path.Combine(outputFolder, "Project_ 1 Tower_A.pdf"), outputPaths.PdfPath);
    }

    private sealed class ReportTestWorkspace : IDisposable
    {
        public string RootPath { get; } = Path.Combine(
            Path.GetTempPath(),
            "ElevateHelperReportTests",
            Guid.NewGuid().ToString("N"));

        public ReportTestWorkspace()
        {
            Directory.CreateDirectory(RootPath);
        }

        public string CreateDirectory(string relativePath)
        {
            string fullPath = Path.Combine(RootPath, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
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
                // Keep test cleanup best-effort.
            }
        }
    }
}
