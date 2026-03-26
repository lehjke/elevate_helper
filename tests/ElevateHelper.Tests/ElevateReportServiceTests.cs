using System.Text;
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

    [Theory]
    [InlineData("$B$2:$H$49", 53, "$B$2:$H$53")]
    [InlineData("$2:$15", 19, "$2:$19")]
    public void UpdateRangeEndRow_UpdatesPrintAddresses(string address, int endRow, string expected)
    {
        string actual = ElevateReportService.UpdateRangeEndRow(address, endRow);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("B", "H", 2, 47, "$B$2:$H$47")]
    [InlineData("B", "AB", 2, 47, "$B$2:$AB$47")]
    [InlineData("B", "Q", 2, 53, "$B$2:$Q$53")]
    public void BuildPrintArea_UsesExactSheetColumns(
        string startColumn,
        string endColumn,
        int startRow,
        int endRow,
        string expected)
    {
        string actual = ElevateReportService.BuildPrintArea(startColumn, endColumn, startRow, endRow);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("3,60", 3.6)]
    [InlineData("3.60", 3.6)]
    [InlineData("1.234,56", 1234.56)]
    public void ParseDoubleFlexible_ParsesLocalizedDecimals(string raw, double expected)
    {
        double actual = ElevateReportService.ParseDoubleFlexible(raw);

        Assert.Equal(expected, actual, 3);
    }

    [Fact]
    public void ReadCsvLines_ReadsWindows1251WithoutMojibake()
    {
        using ReportTestWorkspace workspace = new();
        string csvPath = Path.Combine(workspace.RootPath, "report.csv");
        string expectedLine =
            "\u041C\u0418\u0413 6 \u0416\u0438\u043B\u044C\u0435;\u0433. \u041C\u043E\u0441\u043A\u0432\u0430, \u041B\u0435\u043D\u0438\u043D\u0433\u0440\u0430\u0434\u0441\u043A\u0438\u0439 \u043F\u0440.";

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding encoding1251 = Encoding.GetEncoding(1251);
        File.WriteAllBytes(
            csvPath,
            encoding1251.GetBytes("A;B\r\n" + expectedLine + "\r\n"));

        string[] lines = ElevateReportService.ReadCsvLines(csvPath);

        Assert.Equal(expectedLine, lines[1]);
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
