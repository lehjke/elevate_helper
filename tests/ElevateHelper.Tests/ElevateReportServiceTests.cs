using System.Reflection;
using System.Text;
using System.Xml.Linq;
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
    public void ResolveProjectSourcePath_FallsBackToCsvWhenElvxIsMissing()
    {
        using ReportTestWorkspace workspace = new();
        string reportRoot = workspace.CreateDirectory("probe");
        string projectCsvPath = Path.Combine(reportRoot, "Probe01.csv");
        File.WriteAllText(projectCsvPath, "csv");

        string? resolvedPath = ElevateReportService.ResolveProjectSourcePath(
            "Probe01",
            "Probe01.csv",
            reportRoot);

        Assert.Equal(projectCsvPath, resolvedPath);
    }

    [Fact]
    public void ResolveProjectCsvSourcePath_FindsElevateBatchOutputCsvForElvxSource()
    {
        using ReportTestWorkspace workspace = new();
        string reportRoot = workspace.CreateDirectory("probe");
        string projectCsvPath = Path.Combine(reportRoot, "Probe01_elvx.csv");
        File.WriteAllText(projectCsvPath, "csv");

        string? resolvedPath = ElevateReportService.ResolveProjectCsvSourcePath(
            "Probe01",
            "Probe01.elvx",
            reportRoot);

        Assert.Equal(projectCsvPath, resolvedPath);
    }

    [Theory]
    [InlineData("Probe01.elvx")]
    [InlineData("Probe01.csv")]
    public void ResolveProjectSourcePath_PrefersElvxWhenCsvAlsoExists(string sourceFileName)
    {
        using ReportTestWorkspace workspace = new();
        string reportRoot = workspace.CreateDirectory("probe");
        string projectCsvPath = Path.Combine(reportRoot, "Probe01.csv");
        string projectElvxPath = Path.Combine(reportRoot, "Probe01.elvx");
        File.WriteAllText(projectCsvPath, "csv");
        File.WriteAllText(projectElvxPath, "<ElevateDocument />");

        string? resolvedPath = ElevateReportService.ResolveProjectSourcePath(
            "Probe01",
            sourceFileName,
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
    [InlineData("Кожуховский. Группа 1 (утренний пик)", "R00", "Кожуховский. Группа 1 (утренний пик) R00")]
    [InlineData("Кожуховский. Группа 1 (обеденный пик)", "R07", "Кожуховский. Группа 1 (обеденный пик) R07")]
    [InlineData("Жилой комплекс", "R12", "Жилой комплекс R12")]
    public void BuildOutputPaths_UsesObjectPeakAndRevisionOnly(
        string projectName,
        string revision,
        string expectedBaseName)
    {
        string outputFolder = Path.Combine("C:\\", "Reports");
        ElevateReportService.GeneratedReportPaths outputPaths =
            ElevateReportService.BuildOutputPaths(outputFolder, projectName, revision);

        Assert.Equal(Path.Combine(outputFolder, $"{expectedBaseName}.xlsx"), outputPaths.ExcelPath);
        Assert.Equal(Path.Combine(outputFolder, $"{expectedBaseName}.pdf"), outputPaths.PdfPath);
    }

    [Fact]
    public void BuildOutputPaths_SanitizesWindowsInvalidCharactersOnEveryOs()
    {
        string outputFolder = Path.Combine("C:\\", "Reports");
        ElevateReportService.GeneratedReportPaths outputPaths =
            ElevateReportService.BuildOutputPaths(outputFolder, "A<B>C|D", "E?F*G\"H");

        Assert.Equal(Path.Combine(outputFolder, "A_B_C_D E_F_G_H.xlsx"), outputPaths.ExcelPath);
        Assert.Equal(Path.Combine(outputFolder, "A_B_C_D E_F_G_H.pdf"), outputPaths.PdfPath);
    }

    [Theory]
    [InlineData("morning")]
    [InlineData("lunch")]
    [InlineData("MORNING")]
    public void ResolveReportOutputTarget_UsesProjectRootForOfficeScenarioFolders(string scenarioFolder)
    {
        using ReportTestWorkspace workspace = new();
        string scenarioPath = workspace.CreateDirectory(scenarioFolder);

        ElevateReportService.ReportOutputTarget outputTarget =
            ElevateReportService.ResolveReportOutputTarget(scenarioPath);

        Assert.Equal(workspace.RootPath, outputTarget.OutputFolder);
    }

    [Fact]
    public void ResolveReportOutputTarget_KeepsNonScenarioFolderAsOutputFolder()
    {
        using ReportTestWorkspace workspace = new();
        string projectPath = workspace.CreateDirectory("project");

        ElevateReportService.ReportOutputTarget outputTarget =
            ElevateReportService.ResolveReportOutputTarget(projectPath);

        Assert.Equal(projectPath, outputTarget.OutputFolder);
    }

    [Fact]
    public void ResolveReportOutputTarget_UsesExplicitOutputFolder()
    {
        using ReportTestWorkspace workspace = new();
        string scenarioPath = workspace.CreateDirectory(Path.Combine("project", "morning"));
        string outputFolder = workspace.CreateDirectory("reports");

        ElevateReportService.ReportOutputTarget outputTarget =
            ElevateReportService.ResolveReportOutputTarget(scenarioPath, outputFolder);

        Assert.Equal(outputFolder, outputTarget.OutputFolder);
    }

    [Fact]
    public void ResolveReportOutputTarget_UsesProjectRootForOfficeBatchGroup()
    {
        using ReportTestWorkspace workspace = new();
        string scenarioPath = workspace.CreateDirectory(Path.Combine("Office", "G1", "morning"));

        ElevateReportService.ReportOutputTarget outputTarget =
            ElevateReportService.ResolveReportOutputTarget(scenarioPath, workspace.RootPath);

        Assert.Equal(workspace.RootPath, outputTarget.OutputFolder);
    }

    [Fact]
    public void ResolveReportOutputTarget_UsesProjectRootForNonOfficeBatchGroup()
    {
        using ReportTestWorkspace workspace = new();
        string groupPath = workspace.CreateDirectory(Path.Combine("Res", "G2"));

        ElevateReportService.ReportOutputTarget outputTarget =
            ElevateReportService.ResolveReportOutputTarget(groupPath, workspace.RootPath);

        Assert.Equal(workspace.RootPath, outputTarget.OutputFolder);
    }

    [Theory]
    [InlineData("Office/G1/morning", "Кожуховский. Группа 1 (утренний пик)", "R00")]
    [InlineData("Office/G1/lunch", "Кожуховский. Группа 1 (обеденный пик)", "R07")]
    [InlineData("Res/G2", "Жилой комплекс. Группа 2", "R12")]
    public void BatchReportOutput_UsesJobMetadataWithoutFolderDiscriminator(
        string relativeReportPath,
        string projectName,
        string revision)
    {
        using ReportTestWorkspace workspace = new();
        string reportPath = workspace.CreateDirectory(
            relativeReportPath.Replace('/', Path.DirectorySeparatorChar));

        ElevateReportService.ReportOutputTarget outputTarget =
            ElevateReportService.ResolveReportOutputTarget(reportPath, workspace.RootPath);
        ElevateReportService.GeneratedReportPaths outputPaths =
            ElevateReportService.BuildOutputPaths(outputTarget.OutputFolder, projectName, revision);
        string expectedBaseName = $"{projectName} {revision}";

        Assert.Equal(Path.Combine(workspace.RootPath, $"{expectedBaseName}.xlsx"), outputPaths.ExcelPath);
        Assert.Equal(Path.Combine(workspace.RootPath, $"{expectedBaseName}.pdf"), outputPaths.PdfPath);
    }

    [Fact]
    public void PublishGeneratedReports_ReplacesPairOnlyWhenBothTemporaryFilesExist()
    {
        using ReportTestWorkspace workspace = new();
        string destinationExcelPath = Path.Combine(workspace.RootPath, "report.xlsx");
        string destinationPdfPath = Path.Combine(workspace.RootPath, "report.pdf");
        string temporaryExcelPath = Path.Combine(workspace.RootPath, "temporary.xlsx");
        string temporaryPdfPath = Path.Combine(workspace.RootPath, "temporary.pdf");
        File.WriteAllText(destinationExcelPath, "old excel");
        File.WriteAllText(destinationPdfPath, "old pdf");
        File.WriteAllText(temporaryExcelPath, "new excel");

        Assert.Throws<FileNotFoundException>(() =>
            GeneratedReportPublisher.Publish(
                temporaryExcelPath,
                destinationExcelPath,
                temporaryPdfPath,
                destinationPdfPath));
        Assert.Equal("old excel", File.ReadAllText(destinationExcelPath));
        Assert.Equal("old pdf", File.ReadAllText(destinationPdfPath));

        File.WriteAllText(temporaryPdfPath, "new pdf");
        GeneratedReportPublisher.Publish(
            temporaryExcelPath,
            destinationExcelPath,
            temporaryPdfPath,
            destinationPdfPath);

        Assert.Equal("new excel", File.ReadAllText(destinationExcelPath));
        Assert.Equal("new pdf", File.ReadAllText(destinationPdfPath));
        Assert.False(File.Exists(temporaryExcelPath));
        Assert.False(File.Exists(temporaryPdfPath));
    }

    [Fact]
    public void PublishGeneratedReport_ReplacesOnlyTheRequestedPdf()
    {
        using ReportTestWorkspace workspace = new();
        string destinationPdfPath = Path.Combine(workspace.RootPath, "report.pdf");
        string temporaryPdfPath = Path.Combine(workspace.RootPath, "temporary.pdf");
        File.WriteAllText(destinationPdfPath, "old pdf");
        File.WriteAllText(temporaryPdfPath, "new pdf");

        GeneratedReportPublisher.Publish(temporaryPdfPath, destinationPdfPath);

        Assert.Equal("new pdf", File.ReadAllText(destinationPdfPath));
        Assert.False(File.Exists(temporaryPdfPath));
        Assert.Empty(Directory.EnumerateFiles(workspace.RootPath, "*.xlsx"));
    }

    [Fact]
    public void PublishGeneratedReports_RestoresExcelWhenPdfPublicationFails()
    {
        using ReportTestWorkspace workspace = new();
        string destinationExcelPath = Path.Combine(workspace.RootPath, "report.xlsx");
        string invalidPdfDestination = workspace.CreateDirectory("report.pdf");
        string temporaryExcelPath = Path.Combine(workspace.RootPath, "temporary.xlsx");
        string temporaryPdfPath = Path.Combine(workspace.RootPath, "temporary.pdf");
        File.WriteAllText(destinationExcelPath, "old excel");
        File.WriteAllText(temporaryExcelPath, "new excel");
        File.WriteAllText(temporaryPdfPath, "new pdf");

        Assert.ThrowsAny<IOException>(() =>
            GeneratedReportPublisher.Publish(
                temporaryExcelPath,
                destinationExcelPath,
                temporaryPdfPath,
                invalidPdfDestination));

        Assert.Equal("old excel", File.ReadAllText(destinationExcelPath));
        Assert.True(File.Exists(temporaryPdfPath));
    }

    [Fact]
    public void PublishGeneratedReports_RecoversInterruptedPairBeforePublishing()
    {
        using ReportTestWorkspace workspace = new();
        string destinationExcelPath = Path.Combine(workspace.RootPath, "report.xlsx");
        string destinationPdfPath = Path.Combine(workspace.RootPath, "report.pdf");
        string temporaryExcelPath = Path.Combine(workspace.RootPath, "temporary.xlsx");
        string temporaryPdfPath = Path.Combine(workspace.RootPath, "temporary.pdf");
        const string transactionId = "0123456789abcdef0123456789abcdef";
        string excelBackupPath =
            $"{destinationExcelPath}.elevate-helper-{transactionId}.bak";
        string pdfBackupPath =
            $"{destinationPdfPath}.elevate-helper-{transactionId}.bak";

        File.WriteAllText(excelBackupPath, "old excel");
        File.WriteAllText(pdfBackupPath, "old pdf");
        File.WriteAllText(destinationExcelPath, "partial new excel");
        File.WriteAllText(temporaryExcelPath, "new excel");
        File.WriteAllText(temporaryPdfPath, "new pdf");

        GeneratedReportPublisher.Publish(
            temporaryExcelPath,
            destinationExcelPath,
            temporaryPdfPath,
            destinationPdfPath);

        Assert.Equal("new excel", File.ReadAllText(destinationExcelPath));
        Assert.Equal("new pdf", File.ReadAllText(destinationPdfPath));
        Assert.Empty(Directory.EnumerateFiles(workspace.RootPath, "*.bak"));
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
    [InlineData(1.90, 3.10, 0.0, "1100", "ЦО")]
    [InlineData(2.04, 3.26, 0.0, "1200", "ЦО")]
    [InlineData(2.04, 3.26, 0.5, "1200", "ЦО")]
    public void ResolveDoorInfo_UsesDoorTimingsWithoutPreOpeningTieBreaker(
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
    [InlineData(2.1, 3.6)]
    [InlineData(1.9, 3.2)]
    public void ResolveDoorInfo_ReturnsUnknownForUnsupportedPairs(
        double openTime,
        double closeTime)
    {
        (string width, string type) = ElevateReportService.ResolveDoorInfo(openTime, closeTime);

        Assert.Equal("-", width);
        Assert.Equal("-", type);
    }

    [Theory]
    [InlineData(1.70, 2.80, "900", "ЦО")]
    [InlineData(2.00, 3.30, "1200", "ЦО")]
    [InlineData(2.10, 3.50, "1300", "ЦО")]
    [InlineData(2.20, 3.90, "750", "ТО")]
    [InlineData(2.30, 4.00, "1550", "ЦО")]
    [InlineData(2.30, 4.10, "800", "ТО")]
    [InlineData(2.40, 4.30, "850", "ТО")]
    [InlineData(2.50, 4.50, "900", "ТО")]
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
    [InlineData(2.50, 4.50, 0.5, "900", "ТО")]
    [InlineData(2.20, 3.90, 0.5, "750", "ТО")]
    [InlineData(2.10, 3.50, 0.0, "1300", "ЦО")]
    public void ResolveDoorInfo_IgnoresDoorPreOpeningForDoorSize(
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

    [Theory]
    [InlineData("1.900000", "3.100000", "0.500000", "1100", "ЦО")]
    [InlineData("1.900000", "3.100000", "0.000000", "1100", "ЦО")]
    [InlineData("2.500000", "4.500000", "0.000000", "900", "ТО")]
    [InlineData("2.500000", "4.500000", "0.500000", "900", "ТО")]
    [InlineData("2.200000", "3.900000", "0.500000", "750", "ТО")]
    [InlineData("2.200000", "3.600000", "0.500000", "1350", "ЦО")]
    [InlineData("2.300000", "4.000000", "0.500000", "1550", "ЦО")]
    [InlineData("2.300000", "4.100000", "0.500000", "800", "ТО")]
    [InlineData("3.900000", "7.800000", "0.000000", "1750", "ТО")]
    public void ResolveReportedDoorInfo_UsesElvxDoorAttributes(
        string openTime,
        string closeTime,
        string preOpening,
        string expectedWidth,
        string expectedType)
    {
        (string width, string type) = ElevateReportService.ResolveReportedDoorInfo(openTime, closeTime, preOpening);

        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedType, type);
    }

    [Fact]
    public void ResolveReportedDoorInfo_DoesNotNeedPreOpeningForFormerlyAmbiguousDoor()
    {
        (string width, string type) = ElevateReportService.ResolveReportedDoorInfo("2.500000", "4.500000", null);

        Assert.Equal("900", width);
        Assert.Equal("ТО", type);
    }

    [Fact]
    public void ResolveReportedDoorInfo_MapsRemainingCentralDoorAfterRemovingTelescopic600Door()
    {
        (string width, string type) = ElevateReportService.ResolveReportedDoorInfo("2.000000", "3.300000", "0.000000");

        Assert.Equal("1200", width);
        Assert.Equal("ЦО", type);
    }

    [Fact]
    public void BuildReportEquipmentSpecValues_KeepsDoorAndStartDelayRowsAligned()
    {
        string[,] spec = new string[2, 11];
        spec[1, 1] = "1050";
        spec[1, 2] = "3,00";
        spec[1, 3] = "1,10";
        spec[1, 4] = "1,50";
        spec[1, 6] = "2,00";
        spec[1, 7] = "3,30";
        spec[1, 8] = "1,00";
        spec[1, 9] = "0,50";
        spec[1, 10] = "0,00";
        double[] doorPreOpening = [double.NaN, 0.5];

        string[] actual = ElevateReportService.BuildReportEquipmentSpecValues(
            spec,
            doorPreOpening,
            "ЦО",
            1);

        Assert.Equal(
            [
                "1050",
                "3,00",
                "1,10",
                "1,50",
                "0,50",
                "0,50",
                "2,00",
                "3,30",
                "1,00",
            ],
            actual);
    }

    [Fact]
    public void BuildReportEquipmentSpecValues_UsesTheRequestedElevatorConfiguration()
    {
        string[,] spec = new string[3, 11];
        spec[1, 1] = "1600";
        spec[1, 2] = "2,50";
        spec[2, 1] = "2000";
        spec[2, 2] = "3,00";
        spec[2, 3] = "1,00";
        spec[2, 4] = "1,20";
        spec[2, 6] = "2,00";
        spec[2, 7] = "3,30";
        spec[2, 8] = "2,20";
        spec[2, 9] = "0,60";
        double[] doorPreOpening = [double.NaN, 0.5, 0.3];

        string[] actual = ElevateReportService.BuildReportEquipmentSpecValues(
            spec,
            doorPreOpening,
            "ЦО",
            2);

        Assert.Equal("2000", actual[0]);
        Assert.Equal("3,00", actual[1]);
        Assert.Equal("1,00", actual[2]);
        Assert.Equal("1,20", actual[3]);
        Assert.Equal("0,60", actual[4]);
        Assert.Equal("0,30", actual[5]);
        Assert.Equal("2,00", actual[6]);
        Assert.Equal("3,30", actual[7]);
        Assert.Equal("2,20", actual[8]);
    }

    [Theory]
    [InlineData(double.NaN, "ЦО", "0,50")]
    [InlineData(double.NaN, "ТО", "0,00")]
    [InlineData(0.3, "", "0,30")]
    public void FormatReportedDoorPreOpening_UsesElvxValueOrDoorTypeFallback(
        double value,
        string doorType,
        string expected)
    {
        string actual = ElevateReportService.FormatReportedDoorPreOpening(value, doorType);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(true, "False", 0, false)]
    [InlineData(true, "No", 0, false)]
    [InlineData(true, "False", 125, true)]
    [InlineData(true, "True", 0, true)]
    [InlineData(false, "True", 125, false)]
    public void IsReportServedFloor_ExcludesEmptyNonEntranceFloorsForExpressZones(
        bool isServedByElevator,
        string entranceFloor,
        double noPeople,
        bool expected)
    {
        bool actual = ElevateReportService.IsReportServedFloor(isServedByElevator, entranceFloor, noPeople);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(true, "Yes", true)]
    [InlineData(true, "True", true)]
    [InlineData(true, "1", true)]
    [InlineData(true, "No", false)]
    [InlineData(false, "Yes", false)]
    public void ShouldPrintGroupServedMark_UsesReportServedFloorsForExpressZones(
        bool isReportServedFloor,
        string elevatorServesFloor,
        bool expected)
    {
        bool actual = ElevateReportService.ShouldPrintGroupServedMark(
            isReportServedFloor,
            elevatorServesFloor);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("f", true)]
    [InlineData("Yes", true)]
    [InlineData("True", true)]
    [InlineData("1", true)]
    [InlineData("Rear Doors", true)]
    [InlineData("0", false)]
    [InlineData("No", false)]
    [InlineData("False", false)]
    [InlineData("-", false)]
    public void IsElevatorServesFloor_NormalizesElevateMarks(string value, bool expected)
    {
        bool actual = ElevateReportService.IsElevatorServesFloor(value);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IsFloorServedElement_UsesExplicitFlagBeforeDoorAttribute()
    {
        XElement floorServed = XElement.Parse("<FloorServed FloorIndex=\"7\" Served=\"False\" Doors=\"Front Doors\" />");

        bool actual = ElevateReportService.IsFloorServedElement(floorServed);

        Assert.False(actual);
    }

    [Fact]
    public void IsFloorServedElement_TreatsAnyRealDoorAsServed()
    {
        XElement floorServed = XElement.Parse("<FloorServed FloorIndex=\"7\" Doors=\"Rear Doors\" />");

        bool actual = ElevateReportService.IsFloorServedElement(floorServed);

        Assert.True(actual);
    }

    [Fact]
    public void ParseProjectCsv_ReadsFloorsServedTableInsteadOfDestinationCallStations()
    {
        using ReportTestWorkspace workspace = new();
        string csvPath = Path.Combine(workspace.RootPath, "Project_elvx.csv");
        File.WriteAllText(csvPath, BuildPartialFloorsServedProjectCsv(), Encoding.UTF8);

        MethodInfo parseProjectCsv = typeof(ElevateReportService).GetMethod(
            "ParseProjectCsv",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        object projectData = parseProjectCsv.Invoke(null, [csvPath])!;
        object elevatorData = projectData.GetType().GetProperty("Elevator")!.GetValue(projectData)!;
        string[,] floorsServed = (string[,])elevatorData.GetType().GetProperty("FloorsServed")!.GetValue(elevatorData)!;

        for (int elevator = 1; elevator <= 4; elevator++)
        {
            Assert.Equal("Yes", floorsServed[elevator, 1]);
            Assert.Equal("Yes", floorsServed[elevator, 2]);
        }

        for (int elevator = 5; elevator <= 6; elevator++)
        {
            Assert.Equal("No", floorsServed[elevator, 1]);
            Assert.Equal("No", floorsServed[elevator, 2]);
        }
    }

    [Theory]
    [InlineData(412, 7, 24)]
    [InlineData(412, 7.5, 26)]
    public void CalculateParkingPopulation_RoundsToWholePeople(double totalPeople, double bias, double expected)
    {
        double actual = ElevateReportService.CalculateParkingPopulation(totalPeople, bias);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("3.360000", 0.0, 3.36)]
    [InlineData("", 2.31, 2.31)]
    [InlineData(null, 2.50, 2.50)]
    public void ResolveReportedCabinAreaValue_PrefersElvxAreaAndFallsBack(string? floorAreaText, double fallbackArea, double expected)
    {
        double actual = ElevateReportService.ResolveReportedCabinAreaValue(floorAreaText, fallbackArea);

        Assert.Equal(expected, actual, 3);
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

    private static string BuildPartialFloorsServedProjectCsv()
    {
        string[][] rows = Enumerable.Range(0, 90)
            .Select(_ => Array.Empty<string>())
            .ToArray();

        rows[0] = CsvRow((1, "JOB DATA"));
        rows[1] = CsvRow((4, "Project"));
        rows[2] = CsvRow((4, "R001"));
        rows[3] = CsvRow((4, "Calculation"));
        rows[4] = CsvRow((4, "Author"));
        rows[5] = CsvRow((4, "Checker"));
        rows[6] = CsvRow((4, "Company"));
        rows[7] = CsvRow((4, ""));

        rows[9] = CsvRow((1, "ANALYSIS DATA"));
        rows[12] = CsvRow((6, "Mixed Control (Enhanced ACA)"));

        rows[19] = CsvRow((1, "BUILDING DATA"));
        rows[21] = BuildingFloorRow("Level -2", "3.6", "0", "No");
        rows[22] = BuildingFloorRow("Level -1", "3.6", "0", "No");
        rows[23] = BuildingFloorRow("Level 1", "4.0", "200", "Yes");
        rows[24] = BuildingFloorRow("Level 2", "3.6", "200", "No");
        rows[25] = BuildingFloorRow("Level 3", "3.6", "200", "No");

        rows[30] = CsvRow((6, "20"));
        rows[31] = CsvRow((6, "Office"));
        rows[37] = CsvRow((6, "5"));

        rows[39] = CsvRow((1, "ELEVATOR DATA"));
        rows[40] = ElevatorRow("Car 1", "Car 2", "Car 3", "Car 4", "Car 5", "Car 6");

        string[] specValues = ["1050", "3", "1.1", "1.5", "Level 1", "2", "3.3", "1", "0.5", "0"];
        for (int index = 0; index < specValues.Length; index++)
        {
            rows[41 + index] = ElevatorRow(
                specValues[index],
                specValues[index],
                specValues[index],
                specValues[index],
                specValues[index],
                specValues[index]);
        }

        rows[54] = ElevatorRowWithFloor("Level -2", "1", "1", "1", "1", "1", "1");
        rows[55] = ElevatorRowWithFloor("Level -1", "1", "1", "1", "1", "1", "1");
        rows[56] = ElevatorRowWithFloor("Level 1", "1", "1", "1", "1", "1", "1");
        rows[57] = ElevatorRowWithFloor("Level 2", "1", "1", "1", "1", "1", "1");
        rows[58] = ElevatorRowWithFloor("Level 3", "1", "1", "1", "1", "1", "1");

        rows[60] = ElevatorRowWithFloor("Level -2", "f", "f", "f", "f", "0", "0");
        rows[61] = ElevatorRowWithFloor("Level -1", "f", "f", "f", "f", "0", "0");
        rows[62] = ElevatorRowWithFloor("Level 1", "f", "f", "f", "f", "f", "f");
        rows[63] = ElevatorRowWithFloor("Level 2", "f", "f", "f", "f", "f", "f");
        rows[64] = ElevatorRowWithFloor("Level 3", "f", "f", "f", "f", "f", "f");

        rows[69] = CsvRow((1, "PASSENGER DATA"));
        rows[73] = CsvRow((4, "100"));
        rows[74] = CsvRow((4, "0"));
        rows[75] = CsvRow((4, "0"));

        return string.Join(Environment.NewLine, rows.Select(SerializeCsvRow));
    }

    private static string[] BuildingFloorRow(string floorName, string height, string people, string entranceFloor)
    {
        return CsvRow((1, floorName), (3, height), (5, people), (11, entranceFloor));
    }

    private static string[] ElevatorRow(
        string car1,
        string car2,
        string car3,
        string car4,
        string car5,
        string car6)
    {
        return CsvRow((4, car1), (5, car2), (6, car3), (7, car4), (8, car5), (9, car6));
    }

    private static string[] ElevatorRowWithFloor(
        string floorName,
        string car1,
        string car2,
        string car3,
        string car4,
        string car5,
        string car6)
    {
        return CsvRow(
            (1, floorName),
            (4, car1),
            (5, car2),
            (6, car3),
            (7, car4),
            (8, car5),
            (9, car6));
    }

    private static string[] CsvRow(params (int Column, string Value)[] cells)
    {
        int columnCount = cells.Length == 0
            ? 0
            : cells.Max(cell => cell.Column);

        string[] row = Enumerable.Repeat(string.Empty, columnCount).ToArray();
        foreach ((int column, string value) in cells)
        {
            row[column - 1] = value;
        }

        return row;
    }

    private static string SerializeCsvRow(string[] row)
    {
        return string.Join(';', row);
    }
}
