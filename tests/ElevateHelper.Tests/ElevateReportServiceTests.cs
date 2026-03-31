using System.Text;
using ElevateHelperWinUI.Models;
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

    [Theory]
    [InlineData("Probe01.elvx", null, "Probe01_elvx.csv")]
    [InlineData("Probe01.elvx", 2, "Probe02_elvx.csv")]
    [InlineData("Tower.elvx", 2, "Tower2_elvx.csv")]
    [InlineData("Project001.elvx", 12, "Project012_elvx.csv")]
    public void BuildElevateResultCsvFileName_UsesBatchOutputNaming(string sourceFileName, int? step, string expected)
    {
        string actual = ElevateReportService.BuildElevateResultCsvFileName(sourceFileName, step);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveExistingCsvPath_FindsBatchOutputCsvForProjectRoot()
    {
        using ReportTestWorkspace workspace = new();
        string reportRoot = workspace.CreateDirectory("probe");
        string projectCsvPath = Path.Combine(reportRoot, "Probe01_elvx.csv");
        File.WriteAllText(projectCsvPath, "test");

        string? resolvedPath = ElevateReportService.ResolveExistingCsvPath(
            ElevateReportService.BuildElevateResultCsvFileName("Probe01.elvx"),
            reportRoot);

        Assert.Equal(projectCsvPath, resolvedPath);
    }

    [Fact]
    public void ResolveProjectSourcePath_FindsElvxWhenProjectCsvIsMissing()
    {
        using ReportTestWorkspace workspace = new();
        string reportRoot = workspace.CreateDirectory("probe");
        string projectElvxPath = Path.Combine(reportRoot, "Probe01.elvx");
        File.WriteAllText(projectElvxPath, "<ElevateDocument />");

        string? resolvedPath = ElevateReportService.ResolveProjectSourcePath(
            "Probe01",
            "Probe01.elvx",
            reportRoot);

        Assert.Equal(projectElvxPath, resolvedPath);
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
    [InlineData(BuildingType.Office, "O")]
    [InlineData(BuildingType.Residence, "AB")]
    [InlineData(BuildingType.Hotel, "AB")]
    public void GetAssessmentEndColumn_ReturnsOfficeSpecificRange(BuildingType buildingType, string expected)
    {
        string actual = ElevateReportService.GetAssessmentEndColumn(buildingType);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Титул!Print_Area", true)]
    [InlineData("Здание!Print_Titles", true)]
    [InlineData("Пассажиропоток!Область_печати", true)]
    [InlineData("'Лифтовая группа'!Заголовки_для_печати", true)]
    [InlineData("RandomName", false)]
    public void IsPrintLayoutName_DetectsBuiltInAndLocalizedPrintNames(string name, bool expected)
    {
        bool actual = ElevateReportService.IsPrintLayoutName(name);

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

    [Fact]
    public void NormalizeElevateText_RepairsUtf8Mojibake()
    {
        string actual = ElevateReportService.NormalizeElevateText("Р–РёР»СЊРµ");

        Assert.Equal("Жилье", actual);
    }

    [Theory]
    [InlineData(2.90, 5.70, null, "1200", "ТО")]
    [InlineData(1.90, 3.10, null, "1100", "ЦО")]
    [InlineData(2.04, 3.26, 0.0, "600", "ТО")]
    [InlineData(2.04, 3.26, 0.5, "1200", "ЦО")]
    public void ResolveDoorInfo_UsesDoorTimingsAndPreOpeningTieBreaker(
        double openTime,
        double closeTime,
        double? preOpening,
        string expectedWidth,
        string expectedType)
    {
        (string width, string type) = ElevateReportService.ResolveDoorInfo(openTime, closeTime, preOpening);

        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedType, type);
    }

    [Theory]
    [InlineData(2.0, 3.3)]
    [InlineData(2.1, 3.5)]
    [InlineData(1.9, 3.2)]
    public void ResolveDoorInfo_ReturnsUnknownForAmbiguousOrUnsupportedPairsWithoutPreOpening(
        double openTime,
        double closeTime)
    {
        (string width, string type) = ElevateReportService.ResolveDoorInfo(openTime, closeTime);

        Assert.Equal("-", width);
        Assert.Equal("-", type);
    }

    [Theory]
    [InlineData(1.70, 2.80, "900", "ЦО")]
    [InlineData(2.30, 4.00, "1550", "ЦО")]
    [InlineData(3.90, 7.80, "1750", "ТО")]
    [InlineData(4.00, 8.00, "1800", "ТО")]
    public void ResolveDoorInfo_MapsCorrectedHydraPlusRows(
        double openTime,
        double closeTime,
        string expectedWidth,
        string expectedType)
    {
        (string width, string type) = ElevateReportService.ResolveDoorInfo(openTime, closeTime);

        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedType, type);
    }

    [Theory]
    [InlineData(2.50, 4.50, 0.0, "900", "ТО")]
    [InlineData(2.50, 4.50, 0.5, "1800", "ЦО")]
    public void ResolveDoorInfo_UsesDoorPreOpeningForAmbiguousLargeDoorPairs(
        double openTime,
        double closeTime,
        double preOpening,
        string expectedWidth,
        string expectedType)
    {
        (string width, string type) = ElevateReportService.ResolveDoorInfo(openTime, closeTime, preOpening);

        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedType, type);
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
