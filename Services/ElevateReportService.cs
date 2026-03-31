using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using ElevateHelperWinUI.Models;

namespace ElevateHelperWinUI.Services;

public sealed class ElevateReportService : IElevateReportService
{
    private const string SheetTitle = "Титул";
    private const string SheetBuilding = "Здание";
    private const string SheetFlow = "Пассажиропоток";
    private const string SheetGroup = "Лифтовая группа";
    private const string SheetAssessment = "Оценка";
    private const string SheetCriteria = "Критерии";

    private const int XlShiftDown = -4121;
    private const int XlFormatFromLeftOrAbove = 0;
    private const int XlFormatFromRightOrBelow = 1;
    private const int XlFixedFormatTypePdf = 0;
    private const int XlOpenXmlWorkbook = 51;
    private const int XlWhole = 1;
    private const int XlPart = 2;
    private const int XlValue = 2;

    public async Task<ProcessingResult> PrintReportAsync(
        string path,
        BuildingType buildingType,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () => PrintReportInternal(path, buildingType, cancellationToken),
            cancellationToken);
    }

    private static ProcessingResult PrintReportInternal(
        string path,
        BuildingType buildingType,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return ProcessingResult.Fail("Path is empty.");
            }

            path = NormalizePath(path);
            if (!Directory.Exists(path))
            {
                return ProcessingResult.Fail($"Path does not exist: {path}");
            }

            string batchResultsPath = Path.Combine(path, "batch_results.csv");
            if (!File.Exists(batchResultsPath))
            {
                return ProcessingResult.Fail($"batch_results.csv not found: {batchResultsPath}");
            }

            string? repositoryRoot = FindRepositoryRoot();
            if (repositoryRoot is null)
            {
                return ProcessingResult.Fail("Cannot find repository root containing .example folder.");
            }

            string exampleFolder = Path.Combine(repositoryRoot, ".example");
            string templatePath = Path.Combine(exampleFolder, GetTemplateName(buildingType));
            if (!File.Exists(templatePath))
            {
                return ProcessingResult.Fail($"Template not found: {templatePath}");
            }

            MainBatchData mainData = ParseBatchResults(batchResultsPath);
            if (string.IsNullOrWhiteSpace(mainData.FileName))
            {
                return ProcessingResult.Fail("batch_results.csv does not contain valid project file name (A2).");
            }

            if (string.IsNullOrWhiteSpace(mainData.Folder))
            {
                mainData.Folder = path;
            }

            mainData.Folder = NormalizePath(mainData.Folder);
            string? resolvedBatchFolder = ResolveSearchRoot(path, mainData.Folder);
            string? projectSourcePath = ResolveProjectSourcePath(mainData.FileName, mainData.SourceFileName, path, resolvedBatchFolder);
            if (projectSourcePath is null)
            {
                return ProcessingResult.Fail(
                    $"Project source not found: {DescribeProjectSourceCandidates(mainData.FileName, mainData.SourceFileName)}. Searched under: {DescribeSearchRoots(path, mainData.Folder, resolvedBatchFolder)}");
            }

            mainData.Folder = Path.GetDirectoryName(projectSourcePath)
                ?? throw new InvalidOperationException($"Cannot resolve project source folder for {projectSourcePath}.");

            ProjectParsedData projectData = ParseProjectSource(projectSourcePath);

            int nSteps = mainData.AWT.Length - 1;
            if (nSteps < 1)
            {
                return ProcessingResult.Fail("No data rows found in batch_results.csv.");
            }

            double[] ais = new double[nSteps + 1];
            double[] alw = new double[nSteps + 1];
            for (int step = 1; step <= nSteps; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ParseStepCsv(mainData.FileName, mainData.SourceFileName, path, mainData.Folder, projectData.Building, projectData.Elevator, step, ais, alw);
            }

            GeneratedReportPaths outputPaths = BuildReportWorkbook(
                templatePath,
                path,
                path,
                mainData.AWT,
                mainData.ATTD,
                ais,
                alw,
                projectData.JobData,
                projectData.Building,
                projectData.Elevator,
                projectData.Passenger,
                buildingType,
                cancellationToken);

            return ProcessingResult.Ok(
                $"Report generated: Excel {outputPaths.ExcelPath}; PDF {outputPaths.PdfPath}");
        }
        catch (OperationCanceledException)
        {
            return ProcessingResult.Fail("Report generation was canceled.");
        }
        catch (Exception ex)
        {
            return ProcessingResult.Fail("An exception occurred while generating the report without VBA macro.", ex);
        }
    }

    private static GeneratedReportPaths BuildReportWorkbook(
        string templatePath,
        string outputFolder,
        string xmlFolder,
        double[] awt,
        double[] attd,
        double[] ais,
        double[] alw,
        string[] jobData,
        BuildingDataModel buildingData,
        ElevatorDataModel elevatorData,
        PassengerDataModel passengerData,
        BuildingType buildingType,
        CancellationToken cancellationToken)
    {
        object? excel = null;
        dynamic? workbook = null;

        try
        {
            Type? excelType = Type.GetTypeFromProgID("Excel.Application");
            if (excelType is null)
            {
                throw new InvalidOperationException("Microsoft Excel COM is not available.");
            }

            excel = Activator.CreateInstance(excelType)
                ?? throw new InvalidOperationException("Unable to create Excel COM object.");

            dynamic excelApp = excel;
            excelApp.Visible = false;
            excelApp.DisplayAlerts = false;
            excelApp.ScreenUpdating = false;

            workbook = excelApp.Workbooks.Open(templatePath);

            bool[] isServed = CalculateServedFloors(elevatorData, buildingData.NoFloors, out int servedFloors);

            FillTitleSheet(workbook, jobData);
            FillBuildingSheet(workbook, buildingData, isServed);
            FillFlowSheet(workbook, buildingData, passengerData, isServed);
            FillGroupSheet(workbook, xmlFolder, buildingData, elevatorData);
            FillAssessmentAndCriteriaSheets(workbook, awt, attd, ais, alw, buildingData, elevatorData, passengerData, servedFloors, isServed);
            RemovePrintLayoutNames(workbook);
            ApplyPrintLayout(workbook, buildingType);

            cancellationToken.ThrowIfCancellationRequested();

            GeneratedReportPaths outputPaths = BuildOutputPaths(outputFolder, jobData[1], jobData[2]);

            workbook.Sheets(SheetAssessment).Activate();
            TryDeleteFile(outputPaths.ExcelPath);
            TryDeleteFile(outputPaths.PdfPath);
            workbook.SaveAs(outputPaths.ExcelPath, XlOpenXmlWorkbook);
            workbook.Save();
            workbook.ExportAsFixedFormat(XlFixedFormatTypePdf, outputPaths.PdfPath, Type.Missing, true, false);
            workbook.Close(false);
            ReleaseComObject(ref workbook);
            excelApp.Quit();

            return outputPaths;
        }
        finally
        {
            ReleaseComObject(ref workbook);

            if (excel is not null)
            {
                try
                {
                    Marshal.FinalReleaseComObject(excel);
                }
                catch
                {
                    // Ignore COM cleanup errors.
                }
            }
        }
    }

    private static void FillTitleSheet(dynamic workbook, string[] jobData)
    {
        dynamic sheet = workbook.Sheets(SheetTitle);
        sheet.Cells(24, 5).Value = jobData[1];
        sheet.Cells(26, 5).Value = jobData[3];
        sheet.Cells(28, 5).Value = jobData[2];

        sheet.Rows(30).Delete();
        sheet.Rows(30).Delete();

        sheet.Cells(30, 4).Value = "Исполнитель:";
        sheet.Cells(30, 5).Value = jobData[4];

        sheet.Cells(32, 4).Value = "Дата:";
        sheet.Cells(32, 5).Value = DateTime.Now.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    }

    private static void ApplyPrintLayout(dynamic workbook, BuildingType buildingType)
    {
        int titleLastRow = GetUsedRangeLastRow(workbook.Sheets(SheetTitle));
        ApplySheetPrintLayout(
            workbook.Sheets(SheetTitle),
            BuildPrintArea("B", "H", 2, titleLastRow),
            string.Empty);

        ApplySheetPrintLayout(
            workbook.Sheets(SheetAssessment),
            BuildPrintArea("B", GetAssessmentEndColumn(buildingType), 2, 47),
            string.Empty);

        dynamic groupSheet = workbook.Sheets(SheetGroup);
        int groupLastRow = GetUsedRangeLastRow(groupSheet);
        ApplySheetPrintLayout(
            groupSheet,
            BuildPrintArea("B", "J", 2, groupLastRow),
            "$2:$19");

        dynamic buildingSheet = workbook.Sheets(SheetBuilding);
        int buildingLastRow = GetUsedRangeLastRow(buildingSheet);
        ApplySheetPrintLayout(
            buildingSheet,
            BuildPrintArea("B", "H", 2, buildingLastRow),
            "$2:$3");

        dynamic flowSheet = workbook.Sheets(SheetFlow);
        int flowLastRow = GetUsedRangeLastRow(flowSheet);
        ApplySheetPrintLayout(
            flowSheet,
            BuildPrintArea("B", "Q", 2, flowLastRow),
            "$2:$4");

        ApplySheetPrintLayout(
            workbook.Sheets(SheetCriteria),
            BuildPrintArea("B", "O", 2, 22),
            string.Empty);
    }

    private static void ApplySheetPrintLayout(dynamic sheet, string printArea, string printTitleRows)
    {
        dynamic pageSetup = sheet.PageSetup;
        pageSetup.PrintArea = printArea;
        pageSetup.PrintTitleRows = printTitleRows;
    }

    private static int GetUsedRangeLastRow(dynamic sheet)
    {
        dynamic usedRange = sheet.UsedRange;
        return ToInt(usedRange.Row) + ToInt(usedRange.Rows.Count) - 1;
    }

    internal static string BuildPrintArea(string startColumn, string endColumn, int startRow, int endRow)
    {
        if (string.IsNullOrWhiteSpace(startColumn) || string.IsNullOrWhiteSpace(endColumn))
        {
            throw new ArgumentException("Print area columns must be specified.");
        }

        if (startRow < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(startRow));
        }

        if (endRow < startRow)
        {
            endRow = startRow;
        }

        return $"${startColumn}${startRow}:${endColumn}${endRow}";
    }

    internal static string GetAssessmentEndColumn(BuildingType buildingType)
    {
        return buildingType == BuildingType.Office
            ? "O"
            : "AB";
    }

    private static void RemovePrintLayoutNames(dynamic workbook)
    {
        RemovePrintLayoutNamesFromCollection(workbook.Names);

        foreach (dynamic sheet in workbook.Worksheets)
        {
            RemovePrintLayoutNamesFromCollection(sheet.Names);
        }
    }

    private static void RemovePrintLayoutNamesFromCollection(dynamic names)
    {
        int count = ToInt(names.Count);
        for (int index = count; index >= 1; index--)
        {
            dynamic? name = null;
            try
            {
                name = names.Item(index);
                string normalizedName = Convert.ToString(name.NameLocal, CultureInfo.InvariantCulture)
                    ?? Convert.ToString(name.Name, CultureInfo.InvariantCulture)
                    ?? string.Empty;

                if (IsPrintLayoutName(normalizedName))
                {
                    name.Delete();
                }
            }
            finally
            {
                ReleaseComObject(ref name);
            }
        }
    }

    internal static bool IsPrintLayoutName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return name.Contains("Print_Area", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Print_Titles", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Область_печати", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Заголовки_для_печати", StringComparison.OrdinalIgnoreCase);
    }

    private static void ReleaseComObject<T>(ref T? comObject)
        where T : class
    {
        if (comObject is null)
        {
            return;
        }

        try
        {
            Marshal.FinalReleaseComObject(comObject);
        }
        catch
        {
            // Ignore COM cleanup errors.
        }
        finally
        {
            comObject = null;
        }
    }

    private static void FillBuildingSheet(dynamic workbook, BuildingDataModel buildingData, bool[] isServed)
    {
        dynamic sheet = workbook.Sheets(SheetBuilding);

        for (int i = 1; i <= buildingData.NoFloors; i++)
        {
            InsertRow(sheet, 4, XlFormatFromRightOrBelow);
            sheet.Cells(4, 2).Value = FormatFloorForDisplay(buildingData.FloorName[i]);
            sheet.Cells(4, 3).Value = buildingData.FloorHeight[i];
            sheet.Cells(4, 4).Value = buildingData.FloorLevel[i];
            sheet.Cells(4, 5).Value = buildingData.FloorType[i];
            sheet.Cells(4, 6).Value = buildingData.NoPeople[i];

            if (isServed[i] && buildingData.NoPeople[i] != 0)
            {
                sheet.Cells(4, 7).Value = buildingData.FloorFactor[i];
            }
            else
            {
                sheet.Cells(4, 7).Value = 0;
            }

            sheet.Cells(4, 8).Value = AsDouble(sheet.Cells(4, 6).Value) * AsDouble(sheet.Cells(4, 7).Value);
        }

        sheet.Cells(4, 3).Value = "-";
        sheet.Cells(5 + buildingData.NoFloors, 2).Value = "Итог:";
        sheet.Cells(5 + buildingData.NoFloors, 2).Font.Bold = true;
        sheet.Cells(5 + buildingData.NoFloors, 8).Value = buildingData.CTotalPeople;
        sheet.Cells(5 + buildingData.NoFloors, 8).Font.Bold = true;
    }

    private static void FillFlowSheet(
        dynamic workbook,
        BuildingDataModel buildingData,
        PassengerDataModel passengerData,
        bool[] isServed)
    {
        dynamic sheet = workbook.Sheets(SheetFlow);
        sheet.Cells(3, 3).Value = $"Входящий пассажиропоток{Environment.NewLine}({ToShortPercent(passengerData.Incoming)}%)";
        sheet.Cells(3, 8).Value = $"Выходящий пассажиропоток{Environment.NewLine}({ToShortPercent(passengerData.Outgoing)}%)";
        sheet.Cells(3, 13).Value = $"Межэтажный пассажиропоток{Environment.NewLine}({ToShortPercent(passengerData.Interfloor)}%)";

        for (int i = 1; i <= buildingData.NoFloors; i++)
        {
            InsertRow(sheet, 5, XlFormatFromRightOrBelow);
            sheet.Cells(5, 2).Value = FormatFloorForDisplay(buildingData.FloorName[i]);

            if (passengerData.Incoming != 0)
            {
                if (IsYes(buildingData.EntranceFloor[i]) && buildingData.Bias[i] != 0)
                {
                    SetFormattedNumericCell(sheet, 5, 3, buildingData.Bias[i] / 100d, "0%");
                    sheet.Cells(5, 4).Value = sheet.Cells(2, 19).Value;
                }
                else if (!IsYes(buildingData.EntranceFloor[i]) && isServed[i] && buildingData.NoPeople[i] != 0)
                {
                    sheet.Cells(5, 6).Value = sheet.Cells(2, 19).Value;
                    SetFormattedNumericCell(sheet, 5, 7, buildingData.NoPeople[i] / buildingData.TotalPeople, "0.0%");
                }
            }

            if (passengerData.Outgoing != 0)
            {
                if (!IsYes(buildingData.EntranceFloor[i]) && isServed[i] && buildingData.NoPeople[i] != 0)
                {
                    SetFormattedNumericCell(sheet, 5, 8, buildingData.NoPeople[i] / buildingData.TotalPeople, "0.0%");
                    sheet.Cells(5, 9).Value = sheet.Cells(2, 19).Value;
                }
                else if (IsYes(buildingData.EntranceFloor[i]) && buildingData.Bias[i] != 0)
                {
                    sheet.Cells(5, 11).Value = sheet.Cells(2, 19).Value;
                    SetFormattedNumericCell(sheet, 5, 12, buildingData.Bias[i] / 100d, "0%");
                }
            }

            if (passengerData.Interfloor != 0)
            {
                if (!IsYes(buildingData.EntranceFloor[i]) && isServed[i] && buildingData.NoPeople[i] != 0)
                {
                    SetFormattedNumericCell(sheet, 5, 13, buildingData.NoPeople[i] / buildingData.TotalPeople, "0.0%");
                    sheet.Cells(5, 14).Value = sheet.Cells(2, 19).Value;
                    sheet.Cells(5, 16).Value = sheet.Cells(2, 19).Value;
                    SetFormattedNumericCell(sheet, 5, 17, buildingData.NoPeople[i] / buildingData.TotalPeople, "0.0%");
                }
            }
        }

        if (passengerData.Incoming != 0)
        {
            dynamic incomingRange = sheet.Range(sheet.Cells(5, 5), sheet.Cells(5 + buildingData.NoFloors - 1, 5));
            incomingRange.BorderAround();
        }

        if (passengerData.Outgoing != 0)
        {
            dynamic outgoingRange = sheet.Range(sheet.Cells(5, 10), sheet.Cells(5 + buildingData.NoFloors - 1, 10));
            outgoingRange.BorderAround();
        }

        if (passengerData.Interfloor != 0)
        {
            dynamic interfloorRange = sheet.Range(sheet.Cells(5, 15), sheet.Cells(5 + buildingData.NoFloors - 1, 15));
            interfloorRange.BorderAround();
        }
    }

    private static void FillGroupSheet(
        dynamic workbook,
        string xmlFolder,
        BuildingDataModel buildingData,
        ElevatorDataModel elevatorData)
    {
        dynamic sheet = workbook.Sheets(SheetGroup);
        string dispatcher = elevatorData.Dispatcher.Contains("ACA", StringComparison.OrdinalIgnoreCase) ||
                            elevatorData.Dispatcher.Contains("Double", StringComparison.OrdinalIgnoreCase)
            ? "На этаж назначения (DDS)"
            : "Собирательная при движении вверх и вниз";

        InsertRow(sheet, 14, XlFormatFromLeftOrAbove);
        sheet.Cells(14, 2).Value = "Система управления";
        sheet.Cells(14, 2).HorizontalAlignment = -4131;
        sheet.Cells(14, 2).Font.Bold = true;
        sheet.Cells(14, 3).Value = dispatcher;
        sheet.Cells(14, 3).HorizontalAlignment = -4131;
        sheet.Cells(14, 3).Font.Bold = true;

        for (int i = 1; i <= elevatorData.NoElevators; i++)
        {
            sheet.Cells(4, 2 + i).Value = i;
            for (int j = 1; j <= 9; j++)
            {
                sheet.Cells(4 + j, 2 + i).Value = j < 5
                    ? elevatorData.Spec[i, j]
                    : elevatorData.Spec[i, j + 1];
            }
        }

        for (int i = 1; i <= buildingData.NoFloors; i++)
        {
            InsertRow(sheet, 17, XlFormatFromRightOrBelow);
            sheet.Cells(17, 2).Value = FormatFloorForDisplay(buildingData.FloorName[i]);

            for (int j = 1; j <= elevatorData.NoElevators; j++)
            {
                if (IsYes(elevatorData.FloorsServed[j, i]))
                {
                    sheet.Cells(17, 2 + j).Value = sheet.Cells(2, 13).Value;
                }
                else
                {
                    sheet.Cells(17, 2 + j).Value = sheet.Cells(2, 12).Value;
                }
            }
        }

        InsertRow(sheet, 6, XlFormatFromLeftOrAbove);
        sheet.Cells(6, 2).Value = "Площадь кабины, м2";
        double[] areas = ReadFloorAreas(xmlFolder);
        for (int i = 1; i <= elevatorData.NoElevators; i++)
        {
            SetFormattedNumericCell(sheet, 6, 2 + i, GetFloorAreaByIndex(areas, i), "0.00");
        }

        InsertRow(sheet, 11, XlFormatFromLeftOrAbove);
        sheet.Cells(11, 2).Value = "Ширина дверей, мм";
        InsertRow(sheet, 12, XlFormatFromLeftOrAbove);
        sheet.Cells(12, 2).Value = "Тип дверей (ЦО/ТО)*";

        for (int i = 1; i <= elevatorData.NoElevators; i++)
        {
            double dOpen = ParseDoubleFlexible(elevatorData.Spec[i, 6]);
            double dClose = ParseDoubleFlexible(elevatorData.Spec[i, 7]);
            double? doorPreOpening = double.IsNaN(elevatorData.DoorPreOpening[i])
                ? null
                : elevatorData.DoorPreOpening[i];

            (string width, string type) = ResolveDoorInfo(dOpen, dClose, doorPreOpening);
            sheet.Cells(11, 2 + i).Value = string.IsNullOrWhiteSpace(width)
                ? "-"
                : $"'{width}";
            sheet.Cells(12, 2 + i).Value = type;
        }

        int totalRows = ToInt(sheet.UsedRange.Rows.Count);
        InsertRow(sheet, totalRows + 1, XlFormatFromLeftOrAbove);
        sheet.Cells(totalRows + 2, 2).Value = "*ЦО - центральное открывание, ТО - телескопическое открывание";
    }

    private static void FillAssessmentAndCriteriaSheets(
        dynamic workbook,
        double[] awt,
        double[] attd,
        double[] ais,
        double[] alw,
        BuildingDataModel buildingData,
        ElevatorDataModel elevatorData,
        PassengerDataModel passengerData,
        int servedFloors,
        bool[] isServed)
    {
        dynamic assessmentSheet = workbook.Sheets(SheetAssessment);
        dynamic criteriaSheet = workbook.Sheets(SheetCriteria);

        if (buildingData.BuildingType.Equals("Office", StringComparison.OrdinalIgnoreCase))
        {
            dynamic targetWtCell = assessmentSheet.UsedRange.Find("Target WT", Type.Missing, Type.Missing, XlWhole);
            if (targetWtCell is not null)
            {
                if (NearlyEquals(passengerData.Incoming, 100))
                {
                    targetWtCell.Offset(0, 1).Value = 30;
                }
                else if (NearlyEquals(passengerData.Incoming, 85))
                {
                    targetWtCell.Offset(0, 1).Value = 35;
                }
                else if (NearlyEquals(passengerData.Incoming, 45) || NearlyEquals(passengerData.Incoming, 40))
                {
                    targetWtCell.Offset(0, 1).Value = 40;
                }
            }
        }

        int gLimit;
        int nSteps;
        if (buildingData.BuildingType.Equals("Residential", StringComparison.OrdinalIgnoreCase))
        {
            gLimit = 120;
            nSteps = 8;
        }
        else
        {
            gLimit = 80;
            nSteps = 13;
        }

        int nStepsFromMain = awt.Length - 1;
        ResizeMetrics(ref awt, nSteps, gLimit, nStepsFromMain);
        ResizeMetrics(ref attd, nSteps, gLimit, nStepsFromMain);
        ResizeMetrics(ref ais, nSteps, gLimit, nStepsFromMain);
        ResizeMetrics(ref alw, nSteps, gLimit, nStepsFromMain);

        SortOneBased(awt);
        SortOneBased(attd);
        SortOneBased(ais);
        SortOneBased(alw);

        int lastHC5 = nSteps;
        for (int i = 1; i <= nSteps; i++)
        {
            if (awt[i] > gLimit)
            {
                lastHC5 = i - 1;
                break;
            }
        }

        if (lastHC5 > 0)
        {
            ApplyLinest(ais, lastHC5);
            ApplyLinest(alw, lastHC5);
            SortOneBased(ais);
            SortOneBased(alw);
        }

        dynamic recordCell = assessmentSheet.UsedRange.Find("Record", Type.Missing, Type.Missing, XlWhole);
        if (recordCell is null)
        {
            throw new InvalidOperationException("Cannot find 'Record' marker on assessment sheet.");
        }

        int rCol = ToInt(recordCell.Column);
        int xScale = buildingData.BuildingType.Equals("Residential", StringComparison.OrdinalIgnoreCase) ? 6 : 8;

        int firstOutOfLimitIndex = nSteps + 1;
        for (int i = 1; i <= nSteps; i++)
        {
            if (awt[i] < gLimit)
            {
                assessmentSheet.Cells(7, rCol + i).Value = awt[i];
                assessmentSheet.Cells(9, rCol + i).Value = attd[i];
                assessmentSheet.Cells(11, rCol + i).Value = ais[i];
                assessmentSheet.Cells(13, rCol + i).Value = alw[i];
            }
            else
            {
                firstOutOfLimitIndex = i;
                break;
            }
        }

        int scaleIndex = Math.Max(1, Math.Min(nSteps, firstOutOfLimitIndex - 1));
        int maxScaleAis = (int.Parse(Math.Floor(ais[scaleIndex] / xScale).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) + 2) * xScale;
        int unitAis = Math.Max(1, maxScaleAis / xScale);
        int maxScaleAlw = (int.Parse(Math.Floor(alw[scaleIndex] / xScale).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) + 2) * xScale;
        int unitAlw = Math.Max(1, maxScaleAlw / xScale);

        try
        {
            dynamic chartIs = assessmentSheet.ChartObjects("IS").Chart;
            chartIs.Axes(XlValue).MaximumScale = maxScaleAis;
            chartIs.Axes(XlValue).MajorUnit = unitAis;

            dynamic chartLw = assessmentSheet.ChartObjects("LW").Chart;
            chartLw.Axes(XlValue).MaximumScale = maxScaleAlw;
            chartLw.Axes(XlValue).MajorUnit = unitAlw;
        }
        catch
        {
            // Template may not contain chart objects in some variants.
        }

        if (firstOutOfLimitIndex <= nSteps)
        {
            int prevIndex = Math.Max(1, firstOutOfLimitIndex - 1);
            assessmentSheet.Cells(8, rCol + prevIndex).Value = awt[prevIndex];
            assessmentSheet.Cells(10, rCol + prevIndex).Value = attd[prevIndex];
            assessmentSheet.Cells(12, rCol + prevIndex).Value = ais[prevIndex];
            assessmentSheet.Cells(14, rCol + prevIndex).Value = alw[prevIndex];

            for (int i = firstOutOfLimitIndex; i <= nSteps; i++)
            {
                double hc5 = ToDouble(assessmentSheet.Cells(6, rCol + i).Value);
                assessmentSheet.Cells(8, rCol + i).Value = hc5 * gLimit;
                assessmentSheet.Cells(10, rCol + i).Value = hc5 * gLimit;
                assessmentSheet.Cells(12, rCol + i).Value = hc5 * gLimit;
                assessmentSheet.Cells(14, rCol + i).Value = hc5 * gLimit;
            }
        }

        ApplyServiceFloorsAndExpressZones(workbook, buildingData, isServed);
        ApplyDoubleDispatcherAdjustments(workbook, buildingData, elevatorData, passengerData);

        EvaluateRating(assessmentSheet, criteriaSheet, awt, attd, ais, alw, buildingData, passengerData, gLimit);

        assessmentSheet.Cells(4, 2).Value = BuildElevatorGroupText(elevatorData, servedFloors, buildingData.NoFloors);
        criteriaSheet.Cells(10, 2).Value = BuildFlowText(passengerData);

        int bLength = 29 +
                      ToShortPercent(passengerData.Incoming).Length +
                      ToShortPercent(passengerData.Outgoing).Length +
                      ToShortPercent(passengerData.Interfloor).Length;
        criteriaSheet.Cells(10, 2).Characters(1, bLength).Font.FontStyle = "Bold";
    }

    private static void ApplyServiceFloorsAndExpressZones(
        dynamic workbook,
        BuildingDataModel buildingData,
        bool[] isServed)
    {
        List<int> starts = [];
        List<int> finishes = [];

        for (int i = 2; i <= buildingData.NoFloors - 1; i++)
        {
            if (isServed[i])
            {
                continue;
            }

            if (isServed[i - 1] && isServed[i + 1])
            {
                starts.Add(i);
                finishes.Add(i);
            }
            else if (isServed[i - 1] && !isServed[i + 1])
            {
                starts.Add(i);
            }
            else if (!isServed[i - 1] && isServed[i + 1])
            {
                finishes.Add(i);
            }
        }

        if (starts.Count == 0 || finishes.Count == 0)
        {
            return;
        }

        int pairs = Math.Min(starts.Count, finishes.Count);
        int serviceFloorLimit = 2;
        double expressHeight = 0;

        dynamic buildingSheet = workbook.Sheets(SheetBuilding);
        dynamic flowSheet = workbook.Sheets(SheetFlow);
        dynamic groupSheet = workbook.Sheets(SheetGroup);

        for (int i = 0; i < pairs; i++)
        {
            int startFloorIndex = starts[i];
            int finishFloorIndex = finishes[i];
            if (finishFloorIndex < startFloorIndex)
            {
                continue;
            }

            int rowSpan = finishFloorIndex - startFloorIndex;
            if (rowSpan <= serviceFloorLimit)
            {
                int anchorRow = FindRowByFloorValue(buildingSheet, buildingData.FloorName[startFloorIndex]);
                if (anchorRow > 0)
                {
                    for (int k = 1; k <= rowSpan + 1; k++)
                    {
                        buildingSheet.Cells(anchorRow + 1 - k, 5).Value = "Техэтаж";
                    }
                }

                continue;
            }

            string startFloorText = FormatFloorForDisplay(buildingData.FloorName[startFloorIndex]).TrimStart('\'');
            string finishFloorText = FormatFloorForDisplay(buildingData.FloorName[finishFloorIndex]).TrimStart('\'');
            string rangeValue = $"'{startFloorText} - {finishFloorText}";

            int rowToHide = FindRowByFloorValue(buildingSheet, buildingData.FloorName[startFloorIndex]);
            if (rowToHide > 0)
            {
                buildingSheet.Cells(rowToHide, 5).Value = "Экспресс зона";
                buildingSheet.Cells(rowToHide, 4).Value = "-";

                for (int k = 0; k <= rowSpan; k++)
                {
                    expressHeight += ToDouble(buildingSheet.Cells(rowToHide - k, 3).Value);
                }

                buildingSheet.Cells(rowToHide, 3).Value = expressHeight;
                buildingSheet.Cells(rowToHide, 2).Value = rangeValue;

                int hiddenStart = rowToHide - rowSpan;
                int hiddenEnd = rowToHide - 1;
                if (hiddenStart <= hiddenEnd)
                {
                    buildingSheet.Rows($"{hiddenStart}:{hiddenEnd}").EntireRow.Hidden = true;
                }
            }

            rowToHide = FindRowByFloorValue(flowSheet, buildingData.FloorName[startFloorIndex]);
            if (rowToHide > 0)
            {
                flowSheet.Cells(rowToHide, 2).Value = rangeValue;

                int hiddenStart = rowToHide - rowSpan;
                int hiddenEnd = rowToHide - 1;
                if (hiddenStart <= hiddenEnd)
                {
                    flowSheet.Rows($"{hiddenStart}:{hiddenEnd}").EntireRow.Hidden = true;
                }
            }

            rowToHide = FindRowByFloorValue(groupSheet, buildingData.FloorName[startFloorIndex]);
            if (rowToHide > 0)
            {
                groupSheet.Cells(rowToHide, 2).Value = rangeValue;

                int hiddenStart = rowToHide - rowSpan;
                int hiddenEnd = rowToHide - 1;
                if (hiddenStart <= hiddenEnd)
                {
                    groupSheet.Rows($"{hiddenStart}:{hiddenEnd}").EntireRow.Hidden = true;
                }
            }
        }
    }

    private static void ApplyDoubleDispatcherAdjustments(
        dynamic workbook,
        BuildingDataModel buildingData,
        ElevatorDataModel elevatorData,
        PassengerDataModel passengerData)
    {
        bool isDoubleDispatcher = elevatorData.Dispatcher.Contains("Double", StringComparison.OrdinalIgnoreCase);
        if (!isDoubleDispatcher)
        {
            return;
        }

        dynamic buildingSheet = workbook.Sheets(SheetBuilding);
        int lobbyNumber = 0;

        for (int i = 1; i <= buildingData.NoFloors; i++)
        {
            if (!string.Equals(buildingData.FloorType[i], "Лобби", StringComparison.Ordinal))
            {
                continue;
            }

            lobbyNumber++;
            int rowLobby = FindRowByFloorValue(buildingSheet, buildingData.FloorName[i]);
            if (rowLobby > 0)
            {
                string current = Convert.ToString(buildingSheet.Cells(rowLobby, 5).Value, CultureInfo.InvariantCulture) ?? string.Empty;
                buildingSheet.Cells(rowLobby, 5).Value = $"{current} #{lobbyNumber}";
            }
        }

        dynamic flowSheet = workbook.Sheets(SheetFlow);
        if (passengerData.Incoming != 0)
        {
            MergeAdjacentLobbyRows(flowSheet, buildingData, 3, 4);
        }

        if (passengerData.Outgoing != 0)
        {
            MergeAdjacentLobbyRows(flowSheet, buildingData, 11, 12);
        }

        if (passengerData.Interfloor != 0)
        {
            MergeAdjacentLobbyRows(flowSheet, buildingData, 16, 17);
        }

        dynamic groupSheet = workbook.Sheets(SheetGroup);
        for (int i = 1; i <= elevatorData.NoElevators; i++)
        {
            string currentValue5 = Convert.ToString(groupSheet.Cells(5, i + 2).Value, CultureInfo.InvariantCulture) ?? string.Empty;
            string currentValue6 = Convert.ToString(groupSheet.Cells(6, i + 2).Value, CultureInfo.InvariantCulture) ?? string.Empty;

            groupSheet.Cells(5, i + 2).Value = $"2x{currentValue5}";
            groupSheet.Cells(6, i + 2).Value = $"2x{currentValue6}";
        }
    }

    private static void MergeAdjacentLobbyRows(dynamic flowSheet, BuildingDataModel buildingData, int fromColumn, int toColumn)
    {
        for (int i = 2; i <= buildingData.NoFloors; i++)
        {
            if (!IsYes(buildingData.EntranceFloor[i]) || !IsYes(buildingData.EntranceFloor[i - 1]))
            {
                continue;
            }

            int rowToMerge = FindRowByFloorValue(flowSheet, buildingData.FloorName[i]);
            if (rowToMerge > 0)
            {
                dynamic mergeRange = flowSheet.Range(
                    flowSheet.Cells(rowToMerge, fromColumn),
                    flowSheet.Cells(rowToMerge + 1, fromColumn));
                mergeRange.Merge();

                dynamic mergeRange2 = flowSheet.Range(
                    flowSheet.Cells(rowToMerge, toColumn),
                    flowSheet.Cells(rowToMerge + 1, toColumn));
                mergeRange2.Merge();
            }

            i++;
        }
    }

    private static void EvaluateRating(
        dynamic assessmentSheet,
        dynamic criteriaSheet,
        double[] awt,
        double[] attd,
        double[] ais,
        double[] alw,
        BuildingDataModel buildingData,
        PassengerDataModel passengerData,
        int gLimit)
    {
        assessmentSheet.Cells(47, 2).Value =
            $"{ToShortPercent(passengerData.Incoming)}%, {ToShortPercent(passengerData.Outgoing)}%, {ToShortPercent(passengerData.Interfloor)}%";

        string currentProfile = Convert.ToString(assessmentSheet.Cells(47, 2).Value, CultureInfo.InvariantCulture) ?? string.Empty;

        if (buildingData.BuildingType.Equals("Office", StringComparison.OrdinalIgnoreCase))
        {
            string officeMorning = Convert.ToString(criteriaSheet.Cells(5, 4).Value, CultureInfo.InvariantCulture) ?? string.Empty;
            string officeLunch = Convert.ToString(criteriaSheet.Cells(6, 4).Value, CultureInfo.InvariantCulture) ?? string.Empty;

            if (ProfilesEqual(currentProfile, officeMorning))
            {
                if (awt[13] < 25 && attd[13] < 80)
                {
                    PrintAssessment(assessmentSheet, awt, attd, ais, alw, 13, 5, buildingData);
                }
                else if (awt[12] < 30 && attd[12] < 100)
                {
                    PrintAssessment(assessmentSheet, awt, attd, ais, alw, 12, 4, buildingData);
                }
                else if (awt[11] < 40 && attd[11] < 120)
                {
                    PrintAssessment(assessmentSheet, awt, attd, ais, alw, 11, 3, buildingData);
                }
                else
                {
                    PrintWorstRating(assessmentSheet, awt, attd, ais, alw, 13, 1, gLimit, buildingData);
                }
            }
            else if (ProfilesEqual(currentProfile, officeLunch))
            {
                if (awt[12] < 25 && attd[12] < 80)
                {
                    PrintAssessment(assessmentSheet, awt, attd, ais, alw, 12, 5, buildingData);
                }
                else if (awt[11] < 40 && attd[11] < 100)
                {
                    PrintAssessment(assessmentSheet, awt, attd, ais, alw, 11, 4, buildingData);
                }
                else if (awt[10] < 40 && attd[10] < 120)
                {
                    PrintAssessment(assessmentSheet, awt, attd, ais, alw, 10, 3, buildingData);
                }
                else
                {
                    PrintWorstRating(assessmentSheet, awt, attd, ais, alw, 13, 1, gLimit, buildingData);
                }
            }

            return;
        }

        if (buildingData.BuildingType.Equals("Hotel", StringComparison.OrdinalIgnoreCase))
        {
            string hotelProfile = Convert.ToString(criteriaSheet.Cells(7, 4).Value, CultureInfo.InvariantCulture) ?? string.Empty;
            if (ProfilesEqual(currentProfile, hotelProfile))
            {
                if (awt[13] < 25 && attd[13] < 80)
                {
                    PrintAssessment(assessmentSheet, awt, attd, ais, alw, 13, 5, buildingData);
                }
                else if (awt[12] < 40 && attd[12] < 100)
                {
                    PrintAssessment(assessmentSheet, awt, attd, ais, alw, 12, 4, buildingData);
                }
                else if (awt[11] < 40 && attd[11] < 120)
                {
                    PrintAssessment(assessmentSheet, awt, attd, ais, alw, 11, 3, buildingData);
                }
                else
                {
                    PrintWorstRating(assessmentSheet, awt, attd, ais, alw, 13, 1, gLimit, buildingData);
                }
            }

            return;
        }

        if (!buildingData.BuildingType.Equals("Residential", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string residentialProfile = Convert.ToString(criteriaSheet.Cells(8, 4).Value, CultureInfo.InvariantCulture) ?? string.Empty;
        if (!ProfilesEqual(currentProfile, residentialProfile))
        {
            return;
        }

        if (awt[8] < 40 && attd[8] < 90)
        {
            PrintAssessment(assessmentSheet, awt, attd, ais, alw, 8, 5, buildingData);
        }
        else if (awt[7] < 60 && attd[7] < 120)
        {
            PrintAssessment(assessmentSheet, awt, attd, ais, alw, 7, 4, buildingData);
        }
        else if (awt[6] < 60 && attd[6] < 150)
        {
            PrintAssessment(assessmentSheet, awt, attd, ais, alw, 6, 3, buildingData);
        }
        else
        {
            PrintWorstRating(assessmentSheet, awt, attd, ais, alw, 8, 1, gLimit, buildingData);
        }
    }

    private static void PrintWorstRating(
        dynamic assessmentSheet,
        double[] awt,
        double[] attd,
        double[] ais,
        double[] alw,
        int maxHc5,
        int rating,
        int gLimit,
        BuildingDataModel buildingData)
    {
        for (int i = maxHc5; i >= 1; i--)
        {
            if (awt[i] < gLimit)
            {
                PrintAssessment(assessmentSheet, awt, attd, ais, alw, i, rating, buildingData);
                return;
            }
        }

        PrintAssessment(assessmentSheet, awt, attd, ais, alw, 1, rating, buildingData);
    }

    private static void PrintAssessment(
        dynamic assessmentSheet,
        double[] awt,
        double[] attd,
        double[] ais,
        double[] alw,
        int hc5,
        int rating,
        BuildingDataModel buildingData)
    {
        if (buildingData.BuildingType.Equals("Residential", StringComparison.OrdinalIgnoreCase))
        {
            assessmentSheet.Cells(47, 5).Value = hc5;
            assessmentSheet.Cells(47, 9).Value = awt[hc5];
            assessmentSheet.Cells(47, 13).Value = attd[hc5];
            assessmentSheet.Cells(47, 17).Value = ais[hc5];
            assessmentSheet.Cells(47, 21).Value = alw[hc5];
            assessmentSheet.Cells(47, 25).Value = assessmentSheet.Cells(45, 29 + rating).Value;

            if (rating == 1)
            {
                assessmentSheet.Cells(47, 25).Font.Color = 255;
            }

            return;
        }

        assessmentSheet.Cells(47, 4).Value = hc5;
        assessmentSheet.Cells(47, 6).Value = awt[hc5];
        assessmentSheet.Cells(47, 8).Value = attd[hc5];
        assessmentSheet.Cells(47, 10).Value = ais[hc5];
        assessmentSheet.Cells(47, 12).Value = alw[hc5];
        assessmentSheet.Cells(47, 14).Value = assessmentSheet.Cells(45, 16 + rating).Value;

        if (rating == 1)
        {
            assessmentSheet.Cells(47, 14).Font.Color = 255;
        }
    }

    private static string BuildElevatorGroupText(ElevatorDataModel elevatorData, int servedFloors, int floors)
    {
        Dictionary<string, int> capacityCounts = new(StringComparer.Ordinal);

        for (int i = 1; i <= elevatorData.NoElevators; i++)
        {
            string capacity = elevatorData.Spec[i, 1];
            if (!capacityCounts.TryAdd(capacity, 1))
            {
                capacityCounts[capacity]++;
            }
        }

        string x2 = elevatorData.Dispatcher.Contains("Double", StringComparison.OrdinalIgnoreCase)
            ? "2x"
            : string.Empty;

        System.Text.StringBuilder elevatorsText = new();
        foreach ((string cap, int count) in capacityCounts)
        {
            string noun = count switch
            {
                < 2 => "лифт",
                < 5 => "лифта",
                _ => "лифтов",
            };

            elevatorsText.Append($" {count} {noun} с грузоподъемностью {x2}{cap} кг,");
        }

        return $"Лифтовая группа:{elevatorsText} со скоростью {elevatorData.Spec[1, 2]} м/с. Количество остановок {servedFloors}/{floors}.";
    }

    private static string BuildFlowText(PassengerDataModel passengerData)
    {
        string incoming = ToShortPercent(passengerData.Incoming);
        string outgoing = ToShortPercent(passengerData.Outgoing);
        string interfloor = ToShortPercent(passengerData.Interfloor);

        return
            $"Тип пассажиропотока ({incoming}%, {outgoing}%, {interfloor}%): направление движения пассажиров во время часа пик.{Environment.NewLine}" +
            $"Входной пассажиропоток ({incoming}%) - пассажиропоток с конкретного посадочного этажа при входе в здание.{Environment.NewLine}" +
            $"Выходной пассажиропоток ({outgoing}%) - пассажиропоток с этажей здания на выход из здания.{Environment.NewLine}" +
            $"Межэтажный пассажиропоток ({interfloor}%) - одновременное перемещение пассажиров между этажами здания.";
    }

    private static void ResizeMetrics(ref double[] data, int nSteps, int gLimit, int nStepsFromMain)
    {
        double[] resized = new double[nSteps + 1];

        int copyLength = Math.Min(nSteps, Math.Max(0, data.Length - 1));
        for (int i = 1; i <= copyLength; i++)
        {
            resized[i] = data[i];
        }

        for (int i = Math.Max(1, nStepsFromMain + 1); i <= nSteps; i++)
        {
            resized[i] = i * gLimit;
        }

        data = resized;
    }

    private static bool[] CalculateServedFloors(ElevatorDataModel elevatorData, int noFloors, out int servedFloors)
    {
        bool[] isServed = new bool[noFloors + 1];
        servedFloors = 0;

        for (int floor = 1; floor <= noFloors; floor++)
        {
            for (int elevator = 1; elevator <= elevatorData.NoElevators; elevator++)
            {
                if (!IsYes(elevatorData.FloorsServed[elevator, floor]))
                {
                    continue;
                }

                isServed[floor] = true;
                servedFloors++;
                break;
            }
        }

        return isServed;
    }

    private static void ParseStepCsv(
        string fileName,
        string sourceFileName,
        string reportRoot,
        string projectCsvFolder,
        BuildingDataModel buildingData,
        ElevatorDataModel elevatorData,
        int step,
        double[] ais,
        double[] alw)
    {
        string stepFileName = BuildStepFileName(fileName, step);
        string batchResultStepFileName = BuildElevateResultCsvFileName(sourceFileName, step);
        string? stepCsvPath = ResolveExistingCsvPath(stepFileName, reportRoot, projectCsvFolder);
        stepCsvPath ??= ResolveExistingCsvPath(batchResultStepFileName, reportRoot, projectCsvFolder);
        if (stepCsvPath is null)
        {
            throw new FileNotFoundException(
                $"Step CSV not found: {stepFileName} or {batchResultStepFileName}. Searched under: {DescribeSearchRoots(reportRoot, projectCsvFolder)}",
                stepFileName);
        }

        CsvSheet sheet = CsvSheet.Load(stepCsvPath);

        int breakdownRow = sheet.FindRowContains("breakdown");
        int spatialRow = sheet.FindRowContains("spatial");
        if (breakdownRow == 0 || spatialRow == 0)
        {
            ais[step] = 0;
            alw[step] = 0;
            return;
        }

        int sRow = breakdownRow + 12;
        int eRow = spatialRow - 2;

        int tPass = 0;
        double lw = 0;

        for (int row = sRow; row <= eRow && row <= sheet.RowCount; row++)
        {
            tPass++;
            if (ParseDoubleFlexible(sheet.Get(row, 11)) >= 90)
            {
                lw += 1;
            }
        }

        alw[step] = tPass > 0 ? (100 * lw) / tPass : 0;

        int cRow = spatialRow + 2;
        double[] sAis = new double[elevatorData.NoElevators + 1];

        for (int elevator = 1; elevator <= elevatorData.NoElevators; elevator++)
        {
            double homeFloor = ParseLevelValue(elevatorData.Spec[elevator, 5]);
            int homeFloorIndex = 0;

            for (int j = 1; j <= buildingData.NoFloors; j++)
            {
                if (!NearlyEquals(homeFloor, buildingData.FloorName[j]))
                {
                    continue;
                }

                homeFloorIndex = j;
                break;
            }

            int nrTrip = 0;
            List<int> nrStop = [0];

            while (cRow + 1 <= sheet.RowCount &&
                   ToInt(sheet.Get(cRow, 1)) == elevator &&
                   ToInt(sheet.Get(cRow + 1, 1)) == elevator)
            {
                if (ToInt(sheet.Get(cRow, 3)) == homeFloorIndex &&
                    ToInt(sheet.Get(cRow + 1, 3)) == homeFloorIndex)
                {
                    nrTrip++;
                    nrStop.Add(0);

                    while (cRow + 3 <= sheet.RowCount &&
                           ToInt(sheet.Get(cRow + 2, 3)) != homeFloorIndex &&
                           ToInt(sheet.Get(cRow + 2, 1)) == elevator)
                    {
                        if (ToInt(sheet.Get(cRow + 2, 3)) != ToInt(sheet.Get(cRow + 3, 3)))
                        {
                            nrStop[nrTrip]++;
                        }

                        cRow++;
                    }
                }

                cRow++;
            }

            cRow++;

            int sumStop = 0;
            for (int x = 1; x < nrStop.Count; x++)
            {
                sumStop += nrStop[x];
            }

            sAis[elevator] = nrTrip > 0 ? ((double)sumStop / nrTrip) - 1 : 0;
        }

        double sumAis = 0;
        for (int x = 1; x <= elevatorData.NoElevators; x++)
        {
            sumAis += sAis[x];
        }

        ais[step] = elevatorData.NoElevators > 0 ? sumAis / elevatorData.NoElevators : 0;
    }

    private static ProjectParsedData ParseProjectSource(string projectSourcePath)
    {
        string extension = Path.GetExtension(projectSourcePath);
        if (extension.Equals(".elvx", StringComparison.OrdinalIgnoreCase))
        {
            return ParseProjectElvx(projectSourcePath);
        }

        return ParseProjectCsv(projectSourcePath);
    }

    private static ProjectParsedData ParseProjectElvx(string projectElvxPath)
    {
        XDocument document = XDocument.Load(projectElvxPath, LoadOptions.None);
        XElement root = document.Root
            ?? throw new InvalidOperationException("Project ELVX does not contain a root XML element.");

        XElement jobDataElement = root.Element("JobData")
            ?? throw new InvalidOperationException("JobData was not found in project ELVX.");
        XElement analysisElement = root.Element("AnalysisData")
            ?? throw new InvalidOperationException("AnalysisData was not found in project ELVX.");
        XElement buildingElement = root.Element("BuildingData")
            ?? throw new InvalidOperationException("BuildingData was not found in project ELVX.");
        XElement elevatorElement = root.Element("ElevatorData")
            ?? throw new InvalidOperationException("ElevatorData was not found in project ELVX.");
        XElement passengerElement = root.Element("PassengerData")
            ?? throw new InvalidOperationException("PassengerData was not found in project ELVX.");

        string[] jobData = new string[8];
        jobData[1] = NormalizeElevateText((string?)jobDataElement.Attribute("JobTitle"));
        jobData[2] = NormalizeElevateText((string?)jobDataElement.Attribute("JobNo"));
        jobData[3] = NormalizeElevateText((string?)jobDataElement.Attribute("CalculationTitle"));
        jobData[4] = NormalizeElevateText((string?)jobDataElement.Attribute("MadeBy"));
        jobData[5] = NormalizeElevateText((string?)jobDataElement.Attribute("CheckedBy"));
        jobData[6] = NormalizeElevateText((string?)jobDataElement.Attribute("Company"));
        jobData[7] = string.Empty;

        BuildingDataModel buildingData = ParseBuildingDataFromElvx(buildingElement, passengerElement);
        ElevatorDataModel elevatorData = ParseElevatorDataFromElvx(analysisElement, elevatorElement, buildingData);
        PassengerDataModel passengerData = ParsePassengerDataFromElvx(passengerElement);

        return new ProjectParsedData(jobData, buildingData, elevatorData, passengerData);
    }

    private static ProjectParsedData ParseProjectCsv(string projectCsvPath)
    {
        CsvSheet sheet = CsvSheet.Load(projectCsvPath);

        int jobDataRow = sheet.FindRowExactInColumn(1, "JOB DATA");
        int analysisDataRow = sheet.FindRowExactInColumn(1, "ANALYSIS DATA");
        int buildingDataRow = sheet.FindRowExactInColumn(1, "BUILDING DATA");
        int elevatorDataRow = sheet.FindRowExactInColumn(1, "ELEVATOR DATA");
        int passengerDataRow = sheet.FindRowExactInColumn(1, "PASSENGER DATA");

        if (jobDataRow == 0 || analysisDataRow == 0 || buildingDataRow == 0 || elevatorDataRow == 0 || passengerDataRow == 0)
        {
            throw new InvalidOperationException("Required sections were not found in project CSV.");
        }

        string[] jobData = new string[8];
        for (int i = 1; i <= 7; i++)
        {
            jobData[i] = sheet.Get(jobDataRow + i, 4);
        }

        BuildingDataModel buildingData = new();
        buildingData.BuildingType = sheet.Get(elevatorDataRow - 8, 6);
        buildingData.Absenteeism = ParseDoubleFlexible(sheet.Get(elevatorDataRow - 9, 6));

        double absenteeism = (100 - buildingData.Absenteeism) / 100d;

        buildingData.NoFloors = ToInt(sheet.Get(elevatorDataRow - 2, 6));
        if (buildingData.NoFloors < 1)
        {
            throw new InvalidOperationException("No floors found in project CSV.");
        }

        buildingData.TotalPeople = 0;
        buildingData.CTotalPeople = 0;
        buildingData.NoExitFloors = 0;

        buildingData.FloorName = new double[buildingData.NoFloors + 1];
        buildingData.FloorHeight = new double[buildingData.NoFloors + 1];
        buildingData.FloorLevel = new double[buildingData.NoFloors + 1];
        buildingData.FloorType = new string[buildingData.NoFloors + 1];
        buildingData.FloorFactor = new double[buildingData.NoFloors + 1];
        buildingData.NoPeople = new double[buildingData.NoFloors + 1];
        buildingData.EntranceFloor = new string[buildingData.NoFloors + 1];
        buildingData.Bias = new double[buildingData.NoFloors + 1];

        string buildingTypeLabel = buildingData.BuildingType switch
        {
            "Office" => "Офис",
            "Residential" => "Жилье",
            "Hotel" => "Отель",
            _ => "БКТ",
        };

        for (int i = 1; i <= buildingData.NoFloors; i++)
        {
            string floorNameToken = sheet.Get(buildingDataRow + i + 1, 1);
            buildingData.FloorName[i] = ParseLevelValue(floorNameToken);
            buildingData.FloorHeight[i] = ParseDoubleFlexible(sheet.Get(buildingDataRow + i + 1, 3));
            buildingData.NoPeople[i] = ParseDoubleFlexible(sheet.Get(buildingDataRow + i + 1, 5));
            buildingData.EntranceFloor[i] = sheet.Get(buildingDataRow + i + 1, 11);

            if (buildingData.FloorName[i] < 0)
            {
                buildingData.FloorType[i] = "Парковка";
                buildingData.FloorFactor[i] = 1.2;
            }
            else if (buildingData.FloorName[i] > 0 && IsYes(buildingData.EntranceFloor[i]))
            {
                buildingData.FloorType[i] = "Лобби";
                buildingData.FloorFactor[i] = 0;
            }
            else
            {
                buildingData.FloorType[i] = buildingTypeLabel;
                buildingData.FloorFactor[i] = absenteeism;
                buildingData.NoExitFloors++;
            }

            if (IsYes(buildingData.EntranceFloor[i]))
            {
                for (int j = 1; j <= buildingData.NoFloors; j++)
                {
                    string passengerRowFloor = sheet.Get(passengerDataRow + 15 + j, 1);
                    if (string.Equals(passengerRowFloor, floorNameToken, StringComparison.OrdinalIgnoreCase))
                    {
                        buildingData.Bias[i] = ParseDoubleFlexible(sheet.Get(passengerDataRow + 15 + j, 4));
                        break;
                    }

                    if (!passengerRowFloor.Contains('&'))
                    {
                        continue;
                    }

                    string[] floorParts = passengerRowFloor
                        .Split('&', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

                    if (floorParts.Any(part => string.Equals(part, floorNameToken, StringComparison.OrdinalIgnoreCase)))
                    {
                        buildingData.Bias[i] = ParseDoubleFlexible(sheet.Get(passengerDataRow + 15 + j, 4));
                        break;
                    }
                }
            }

            buildingData.TotalPeople += buildingData.NoPeople[i];
            buildingData.CTotalPeople += buildingData.NoPeople[i] * buildingData.FloorFactor[i];
        }

        double lowerLevel = 0;
        if (buildingData.FloorName[1] < 1)
        {
            for (int i = 1; i <= Math.Abs((int)buildingData.FloorName[1]) && i <= buildingData.NoFloors; i++)
            {
                lowerLevel -= buildingData.FloorHeight[i];
            }
        }

        buildingData.FloorLevel[1] = lowerLevel;
        for (int i = 2; i <= buildingData.NoFloors; i++)
        {
            buildingData.FloorLevel[i] = buildingData.FloorLevel[i - 1] + buildingData.FloorHeight[i - 1];
        }

        for (int i = 1; i <= buildingData.NoFloors; i++)
        {
            if (string.Equals(buildingData.FloorType[i], "Парковка", StringComparison.Ordinal))
            {
                buildingData.NoPeople[i] = (buildingData.TotalPeople * buildingData.Bias[i]) / 120d;
            }
        }

        ElevatorDataModel elevatorData = new();
        elevatorData.Dispatcher = sheet.Get(analysisDataRow + 3, 6);

        elevatorData.NoElevators = 0;
        for (int i = 1; i <= sheet.ColumnCount; i++)
        {
            if (!string.IsNullOrWhiteSpace(sheet.Get(elevatorDataRow + 1, 3 + i)))
            {
                elevatorData.NoElevators++;
            }
        }

        if (elevatorData.NoElevators < 1)
        {
            throw new InvalidOperationException("No elevators were found in project CSV.");
        }

        elevatorData.Spec = new string[elevatorData.NoElevators + 1, 11];
        elevatorData.DoorPreOpening = CreateDoorPreOpeningArray(elevatorData.NoElevators);
        for (int i = 1; i <= elevatorData.NoElevators; i++)
        {
            for (int j = 1; j <= 10; j++)
            {
                elevatorData.Spec[i, j] = sheet.Get(elevatorDataRow + 1 + j, 3 + i);
            }
        }

        elevatorData.FloorsServed = new string[elevatorData.NoElevators + 1, buildingData.NoFloors + 1];
        for (int i = 1; i <= elevatorData.NoElevators; i++)
        {
            for (int j = 1; j <= buildingData.NoFloors; j++)
            {
                elevatorData.FloorsServed[i, j] = sheet.Get(elevatorDataRow + 14 + j, 3 + i);
            }
        }

        PassengerDataModel passengerData = new()
        {
            Incoming = ParseDoubleFlexible(sheet.Get(passengerDataRow + 4, 4)),
            Outgoing = ParseDoubleFlexible(sheet.Get(passengerDataRow + 5, 4)),
            Interfloor = ParseDoubleFlexible(sheet.Get(passengerDataRow + 6, 4)),
        };

        return new ProjectParsedData(jobData, buildingData, elevatorData, passengerData);
    }

    private static BuildingDataModel ParseBuildingDataFromElvx(XElement buildingElement, XElement passengerElement)
    {
        BuildingDataModel buildingData = new();
        buildingData.BuildingType = NormalizeBuildingType((string?)buildingElement.Attribute("BuildingType"));
        buildingData.Absenteeism = ParseDoubleFlexible((string?)buildingElement.Attribute("AbsenteeismPercent"));

        List<XElement> floorElements = buildingElement.Elements("Floor").ToList();
        if (floorElements.Count == 0)
        {
            throw new InvalidOperationException("No floors were found in project ELVX.");
        }

        buildingData.NoFloors = floorElements.Count;
        buildingData.FloorName = new double[buildingData.NoFloors + 1];
        buildingData.FloorHeight = new double[buildingData.NoFloors + 1];
        buildingData.FloorLevel = new double[buildingData.NoFloors + 1];
        buildingData.FloorType = new string[buildingData.NoFloors + 1];
        buildingData.FloorFactor = new double[buildingData.NoFloors + 1];
        buildingData.NoPeople = new double[buildingData.NoFloors + 1];
        buildingData.EntranceFloor = new string[buildingData.NoFloors + 1];
        buildingData.Bias = new double[buildingData.NoFloors + 1];

        List<XElement> biasElements = passengerElement
            .Element("Standard")?
            .Elements("Floor")
            .ToList() ?? [];

        double absenteeismFactor = (100 - buildingData.Absenteeism) / 100d;
        string buildingTypeLabel = buildingData.BuildingType switch
        {
            "Office" => "Офис",
            "Residential" => "Жилье",
            "Hotel" => "Гостиница",
            _ => "Здание",
        };

        double[] absoluteLevels = new double[buildingData.NoFloors + 1];
        for (int i = 1; i <= buildingData.NoFloors; i++)
        {
            XElement floorElement = floorElements[i - 1];
            buildingData.FloorName[i] = ParseLevelValue((string?)floorElement.Attribute("FloorName") ?? string.Empty);
            absoluteLevels[i] = ParseDoubleFlexible((string?)floorElement.Attribute("FloorLevel"));
            buildingData.NoPeople[i] = ParseDoubleFlexible((string?)floorElement.Attribute("NoOfPeople"));
            buildingData.EntranceFloor[i] = ((string?)floorElement.Attribute("EntranceFloor")) ?? string.Empty;
            buildingData.Bias[i] = i <= biasElements.Count
                ? ParseDoubleFlexible((string?)biasElements[i - 1].Attribute("EntranceBias"))
                : 0;

            if (buildingData.FloorName[i] < 0)
            {
                buildingData.FloorType[i] = "Парковка";
                buildingData.FloorFactor[i] = 1.2;
            }
            else if (buildingData.FloorName[i] > 0 && IsYes(buildingData.EntranceFloor[i]))
            {
                buildingData.FloorType[i] = "Лобби";
                buildingData.FloorFactor[i] = 0;
            }
            else
            {
                buildingData.FloorType[i] = buildingTypeLabel;
                buildingData.FloorFactor[i] = absenteeismFactor;
                buildingData.NoExitFloors++;
            }

            buildingData.TotalPeople += buildingData.NoPeople[i];
            buildingData.CTotalPeople += buildingData.NoPeople[i] * buildingData.FloorFactor[i];
        }

        int referenceFloorIndex = Enumerable.Range(1, buildingData.NoFloors)
            .FirstOrDefault(i => NearlyEquals(buildingData.FloorName[i], 1) && IsYes(buildingData.EntranceFloor[i]));
        if (referenceFloorIndex == 0)
        {
            referenceFloorIndex = Enumerable.Range(1, buildingData.NoFloors)
                .FirstOrDefault(i => IsYes(buildingData.EntranceFloor[i]));
        }

        if (referenceFloorIndex == 0)
        {
            referenceFloorIndex = 1;
        }

        double referenceLevel = absoluteLevels[referenceFloorIndex];
        for (int i = 1; i <= buildingData.NoFloors; i++)
        {
            buildingData.FloorLevel[i] = absoluteLevels[i] - referenceLevel;
            if (i < buildingData.NoFloors)
            {
                buildingData.FloorHeight[i] = Math.Abs(absoluteLevels[i + 1] - absoluteLevels[i]);
            }
            else
            {
                buildingData.FloorHeight[i] = i > 1
                    ? buildingData.FloorHeight[i - 1]
                    : 0;
            }
        }

        for (int i = 1; i <= buildingData.NoFloors; i++)
        {
            if (string.Equals(buildingData.FloorType[i], "Парковка", StringComparison.Ordinal))
            {
                buildingData.NoPeople[i] = (buildingData.TotalPeople * buildingData.Bias[i]) / 120d;
            }
        }

        return buildingData;
    }

    private static ElevatorDataModel ParseElevatorDataFromElvx(
        XElement analysisElement,
        XElement elevatorElement,
        BuildingDataModel buildingData)
    {
        XElement configurationElement = elevatorElement
            .Element("Advanced")?
            .Element("Configuration")
            ?? throw new InvalidOperationException("Advanced elevator configuration was not found in project ELVX.");

        List<XElement> carElements = configurationElement.Elements("Car").ToList();
        if (carElements.Count == 0)
        {
            throw new InvalidOperationException("No elevators were found in project ELVX.");
        }

        ElevatorDataModel elevatorData = new();
        elevatorData.Dispatcher = NormalizeElevateText(
            (string?)analysisElement.Element("Dispatcher")?.Element("Algorithm")?.Attribute("AlgorithmName"));
        elevatorData.NoElevators = carElements.Count;
        elevatorData.Spec = new string[elevatorData.NoElevators + 1, 11];
        elevatorData.DoorPreOpening = CreateDoorPreOpeningArray(elevatorData.NoElevators);
        elevatorData.FloorsServed = new string[elevatorData.NoElevators + 1, buildingData.NoFloors + 1];

        for (int i = 1; i <= elevatorData.NoElevators; i++)
        {
            XElement carElement = carElements[i - 1];
            elevatorData.Spec[i, 1] = FormatNumericSpec((string?)carElement.Attribute("Capacity"), "0");
            elevatorData.Spec[i, 2] = FormatNumericSpec((string?)carElement.Attribute("Speed"), "0.00");
            elevatorData.Spec[i, 3] = FormatNumericSpec((string?)carElement.Attribute("Acceleration"), "0.00");
            elevatorData.Spec[i, 4] = FormatNumericSpec((string?)carElement.Attribute("Jerk"), "0.00");

            int homeFloorIndex = ToInt(((string?)carElement.Attribute("HomeFloor")) ?? string.Empty);
            elevatorData.Spec[i, 5] = homeFloorIndex >= 1 && homeFloorIndex <= buildingData.NoFloors
                ? FormatFloorForDisplay(buildingData.FloorName[homeFloorIndex])
                : ((string?)carElement.Attribute("HomeFloor")) ?? string.Empty;

            elevatorData.Spec[i, 6] = FormatNumericSpec((string?)carElement.Attribute("DoorOpenTime"), "0.00");
            elevatorData.Spec[i, 7] = FormatNumericSpec((string?)carElement.Attribute("DoorCloseTime"), "0.00");
            elevatorData.Spec[i, 8] = FormatNumericSpec((string?)carElement.Attribute("DoorDwell1"), "0.00");
            elevatorData.Spec[i, 9] = FormatNumericSpec((string?)carElement.Attribute("MotorStartDelay"), "0.00");
            elevatorData.Spec[i, 10] = FormatNumericSpec((string?)carElement.Attribute("LevelingDelay"), "0.00");
            elevatorData.DoorPreOpening[i] = ParseDoubleOrNaN((string?)carElement.Attribute("DoorPreOpening"));

            foreach (XElement floorServedElement in carElement.Elements("FloorServed"))
            {
                int floorIndex = ToInt(((string?)floorServedElement.Attribute("FloorIndex")) ?? string.Empty);
                if (floorIndex >= 1 && floorIndex <= buildingData.NoFloors)
                {
                    elevatorData.FloorsServed[i, floorIndex] = "Yes";
                }
            }
        }

        return elevatorData;
    }

    private static PassengerDataModel ParsePassengerDataFromElvx(XElement passengerElement)
    {
        XElement? trafficPeriod = passengerElement
            .Element("Traffic")?
            .Elements("Period")
            .FirstOrDefault(period => string.Equals((string?)period.Attribute("Id"), "0", StringComparison.Ordinal));
        XElement? standardElement = passengerElement.Element("Standard");

        return new PassengerDataModel
        {
            Incoming = trafficPeriod is not null
                ? ParseDoubleFlexible((string?)trafficPeriod.Attribute("SplitUp"))
                : ParseDoubleFlexible(standardElement?.Element("Incoming")?.Value),
            Outgoing = trafficPeriod is not null
                ? ParseDoubleFlexible((string?)trafficPeriod.Attribute("SplitDown"))
                : ParseDoubleFlexible(standardElement?.Element("Outgoing")?.Value),
            Interfloor = trafficPeriod is not null
                ? ParseDoubleFlexible((string?)trafficPeriod.Attribute("SplitInterfloor"))
                : ParseDoubleFlexible(standardElement?.Element("Interfloor")?.Value),
        };
    }

    private static string NormalizeBuildingType(string? rawValue)
    {
        return (rawValue ?? string.Empty).Trim() switch
        {
            "1" => "Office",
            "2" => "Hotel",
            "3" => "Residential",
            string value when value.Equals("Office", StringComparison.OrdinalIgnoreCase) => "Office",
            string value when value.Equals("Hotel", StringComparison.OrdinalIgnoreCase) => "Hotel",
            string value when value.Equals("Residential", StringComparison.OrdinalIgnoreCase) => "Residential",
            _ => string.Empty,
        };
    }

    private static string FormatNumericSpec(string? rawValue, string format)
    {
        double value = ParseDoubleFlexible(rawValue);
        return value.ToString(format, CultureInfo.GetCultureInfo("ru-RU"));
    }

    private static double[] CreateDoorPreOpeningArray(int noElevators)
    {
        double[] values = new double[noElevators + 1];
        Array.Fill(values, double.NaN);
        return values;
    }

    private static double ParseDoubleOrNaN(string? rawValue)
    {
        return string.IsNullOrWhiteSpace(rawValue)
            ? double.NaN
            : ParseDoubleFlexible(rawValue);
    }

    internal static string NormalizeElevateText(string? rawValue)
    {
        string value = rawValue?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (!LooksLikeUtf8Mojibake(value))
        {
            return value;
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string repaired = Encoding.UTF8.GetString(Encoding.GetEncoding(1251).GetBytes(value));
        return CountMojibakeMarkers(repaired) < CountMojibakeMarkers(value) ||
               CountCyrillicLetters(repaired) > CountCyrillicLetters(value)
            ? repaired
            : value;
    }

    private static bool LooksLikeUtf8Mojibake(string value)
    {
        return value.Contains('Р') || value.Contains('С');
    }

    private static int CountCyrillicLetters(string value)
    {
        int count = 0;
        foreach (char c in value)
        {
            if ((c >= '\u0400' && c <= '\u04FF') || c == 'Ё' || c == 'ё')
            {
                count++;
            }
        }

        return count;
    }

    private static int CountMojibakeMarkers(string value)
    {
        int count = 0;
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (current == '\uFFFD' || current == 'Ð' || current == 'Ñ')
            {
                count += 3;
                continue;
            }

            if (current is 'Р' or 'С')
            {
                count++;
                if (index + 1 < value.Length && !char.IsWhiteSpace(value[index + 1]) && !char.IsLetter(value[index + 1]))
                {
                    count += 2;
                }
            }
        }

        return count;
    }

    private static MainBatchData ParseBatchResults(string batchResultsPath)
    {
        CsvSheet sheet = CsvSheet.Load(batchResultsPath);

        List<int> dataRows = [];
        for (int row = 2; row <= sheet.RowCount; row++)
        {
            if (!string.IsNullOrWhiteSpace(sheet.Get(row, 1)) ||
                !string.IsNullOrWhiteSpace(sheet.Get(row, 7)) ||
                !string.IsNullOrWhiteSpace(sheet.Get(row, 11)))
            {
                dataRows.Add(row);
            }
        }

        if (dataRows.Count == 0 && sheet.RowCount >= 2)
        {
            dataRows.Add(2);
        }

        int steps = dataRows.Count;
        double[] awt = new double[steps + 1];
        double[] attd = new double[steps + 1];

        for (int i = 1; i <= steps; i++)
        {
            int row = dataRows[i - 1];
            awt[i] = ParseDoubleFlexible(sheet.Get(row, 7));
            attd[i] = ParseDoubleFlexible(sheet.Get(row, 11));
        }

        int keyRow = dataRows.Count > 0 ? dataRows[0] : 2;
        string sourceFileName = Path.GetFileName(sheet.Get(keyRow, 1).Trim());
        string fileName = Path.GetFileNameWithoutExtension(sourceFileName);

        return new MainBatchData
        {
            FileName = fileName,
            SourceFileName = sourceFileName,
            Folder = sheet.Get(keyRow, 2),
            AWT = awt,
            ATTD = attd,
        };
    }

    private static string? FindRepositoryRoot()
    {
        string[] roots =
        [
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory(),
        ];

        foreach (string root in roots)
        {
            DirectoryInfo? current = new(root);
            while (current is not null)
            {
                string exampleDir = Path.Combine(current.FullName, ".example");
                if (Directory.Exists(exampleDir))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        return null;
    }

    private static string GetTemplateName(BuildingType buildingType)
    {
        return buildingType switch
        {
            BuildingType.Office => "Office.xlsx",
            BuildingType.Hotel => "Hotel.xlsx",
            BuildingType.Residence => "Residential.xlsx",
            _ => throw new ArgumentOutOfRangeException(nameof(buildingType), buildingType, "Unsupported building type."),
        };
    }

    internal static string BuildStepFileName(string fileName, int step)
    {
        string cleanFileName = Path.GetFileNameWithoutExtension(fileName.Trim());

        string prefix = cleanFileName.Length > 3
            ? cleanFileName[..^3]
            : cleanFileName;

        prefix = prefix.TrimEnd();
        return step < 10
            ? $"{prefix} 0{step}.csv"
            : $"{prefix} {step}.csv";
    }

    internal static string BuildElevateResultCsvFileName(string sourceFileName, int? step = null)
    {
        string cleanSourceFileName = Path.GetFileName(sourceFileName.Trim());
        string sourceStem = Path.GetFileNameWithoutExtension(cleanSourceFileName);
        string targetStem = step.HasValue
            ? BuildSequentialStem(sourceStem, step.Value)
            : sourceStem;

        return $"{targetStem}_elvx.csv";
    }

    private static string NormalizePath(string path)
    {
        string normalized = path.Trim().Trim('"').Replace('/', '\\');
        return normalized.TrimEnd('\\');
    }

    internal static GeneratedReportPaths BuildOutputPaths(string outputFolder, string projectName, string buildingName)
    {
        string sanitizedBaseName = SanitizeFileName($"{projectName} {buildingName}");
        return new GeneratedReportPaths(
            Path.Combine(outputFolder, $"{sanitizedBaseName}.xlsx"),
            Path.Combine(outputFolder, $"{sanitizedBaseName}.pdf"));
    }

    internal static string? ResolveExistingCsvPath(string targetFileName, params string?[] searchRoots)
    {
        if (string.IsNullOrWhiteSpace(targetFileName))
        {
            return null;
        }

        if (Path.IsPathRooted(targetFileName) && File.Exists(targetFileName))
        {
            return targetFileName;
        }

        string cleanTargetFileName = Path.GetFileName(targetFileName.Trim());

        foreach (string root in ExpandSearchRoots(searchRoots))
        {
            string directPath = Path.Combine(root, cleanTargetFileName);
            if (File.Exists(directPath))
            {
                return directPath;
            }
        }

        foreach (string root in ExpandSearchRoots(searchRoots))
        {
            string? recursivePath = TryFindFileRecursive(root, cleanTargetFileName);
            if (recursivePath is not null)
            {
                return recursivePath;
            }
        }

        return null;
    }

    internal static string DescribeSearchRoots(params string?[] searchRoots)
    {
        string[] roots = ExpandSearchRoots(searchRoots).ToArray();
        return roots.Length == 0
            ? "(no valid roots)"
            : string.Join("; ", roots);
    }

    internal static string UpdateRangeEndRow(string rangeAddress, int endRow)
    {
        if (string.IsNullOrWhiteSpace(rangeAddress) || endRow < 1)
        {
            return rangeAddress;
        }

        string[] parts = rangeAddress.Split(':', 2);
        if (parts.Length != 2)
        {
            return rangeAddress;
        }

        string endPart = parts[1];
        int lastDollar = endPart.LastIndexOf('$');
        if (lastDollar < 0)
        {
            return rangeAddress;
        }

        string prefix = endPart[..lastDollar];
        return $"{parts[0]}:{prefix}${endRow}";
    }

    private static string SanitizeFileName(string name)
    {
        string result = name;
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            result = result.Replace(invalid, '_');
        }

        if (string.IsNullOrWhiteSpace(result))
        {
            return "report";
        }

        return result;
    }

    private static IEnumerable<string> ExpandSearchRoots(params string?[] searchRoots)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (string? root in searchRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            string normalized = NormalizePath(root);
            if (Directory.Exists(normalized) && seen.Add(normalized))
            {
                yield return normalized;
            }

            if (!Path.IsPathRooted(normalized))
            {
                continue;
            }

            string? parent = Path.GetDirectoryName(normalized);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                parent = NormalizePath(parent);
                if (Directory.Exists(parent) && seen.Add(parent))
                {
                    yield return parent;
                }
            }
        }
    }

    private static string? ResolveSearchRoot(string basePath, string? searchRoot)
    {
        if (string.IsNullOrWhiteSpace(searchRoot))
        {
            return null;
        }

        string normalized = NormalizePath(searchRoot);
        return Path.IsPathRooted(normalized)
            ? normalized
            : NormalizePath(Path.Combine(basePath, normalized));
    }

    private static string? TryFindFileRecursive(string root, string targetFileName)
    {
        try
        {
            return Directory
                .EnumerateFiles(root, targetFileName, SearchOption.AllDirectories)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static void InsertRow(dynamic sheet, int row, int copyOrigin)
    {
        try
        {
            sheet.Rows(row).Insert(XlShiftDown, copyOrigin);
        }
        catch
        {
            sheet.Rows(row).Insert();
        }
    }

    private static void SetFormattedNumericCell(dynamic sheet, int row, int column, double value, string format)
    {
        dynamic cell = sheet.Cells(row, column);
        cell.Value = value;

        if (TryApplyNumberFormat(cell, format))
        {
            return;
        }

        cell.Value = FormatNumericText(value, format);
    }

    private static bool TryApplyNumberFormat(dynamic cell, string format)
    {
        try
        {
            cell.NumberFormat = format;
            return true;
        }
        catch
        {
            try
            {
                cell.NumberFormatLocal = ToLocalNumberFormat(format);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private static string ToLocalNumberFormat(string format)
    {
        string separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        return separator == "."
            ? format
            : format.Replace(".", separator, StringComparison.Ordinal);
    }

    private static string FormatNumericText(double value, string format)
    {
        return format switch
        {
            "0%" => value.ToString("0%", CultureInfo.CurrentCulture),
            "0.0%" => value.ToString("0.0%", CultureInfo.CurrentCulture),
            "0.00" => value.ToString("0.00", CultureInfo.CurrentCulture),
            _ => value.ToString(CultureInfo.CurrentCulture),
        };
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore stale output cleanup errors and let Excel surface a save error if needed.
        }
    }

    private static double[] ReadFloorAreas(string xmlFolder)
    {
        string path = Path.Combine(xmlFolder, "floor_area.csv");
        if (!File.Exists(path))
        {
            return [0];
        }

        CsvSheet sheet = CsvSheet.Load(path);
        List<double> values = [0];

        for (int row = 2; row <= sheet.RowCount; row++)
        {
            values.Add(ParseDoubleFlexible(sheet.Get(row, 2)));
        }

        return values.ToArray();
    }

    private static double GetFloorAreaByIndex(double[] areas, int index)
    {
        return index >= 1 && index < areas.Length
            ? areas[index]
            : 0;
    }

    private static readonly (int OpenKey, int CloseKey, string Width, string Type)[] DoorTimingRules =
    [
        (20, 33, "600", "ТО"),
        (21, 35, "650", "ТО"),
        (21, 37, "700", "ТО"),
        (22, 39, "750", "ТО"),
        (23, 41, "800", "ТО"),
        (24, 43, "850", "ТО"),
        (25, 45, "900", "ТО"),
        (26, 47, "950", "ТО"),
        (26, 49, "1000", "ТО"),
        (27, 51, "1050", "ТО"),
        (28, 53, "1100", "ТО"),
        (29, 55, "1150", "ТО"),
        (29, 57, "1200", "ТО"),
        (30, 59, "1250", "ТО"),
        (31, 60, "1300", "ТО"),
        (32, 62, "1350", "ТО"),
        (33, 64, "1400", "ТО"),
        (34, 66, "1450", "ТО"),
        (35, 68, "1500", "ТО"),
        (36, 70, "1550", "ТО"),
        (36, 72, "1600", "ТО"),
        (37, 74, "1650", "ТО"),
        (38, 76, "1700", "ТО"),
        (39, 78, "1750", "ТО"),
        (40, 80, "1800", "ТО"),
        (15, 22, "600", "ЦО"),
        (16, 23, "650", "ЦО"),
        (16, 24, "700", "ЦО"),
        (17, 25, "750", "ЦО"),
        (17, 26, "800", "ЦО"),
        (17, 27, "850", "ЦО"),
        (17, 28, "900", "ЦО"),
        (18, 29, "1000", "ЦО"),
        (19, 30, "1050", "ЦО"),
        (19, 31, "1100", "ЦО"),
        (20, 32, "1150", "ЦО"),
        (20, 33, "1200", "ЦО"),
        (21, 34, "1250", "ЦО"),
        (21, 35, "1300", "ЦО"),
        (22, 36, "1350", "ЦО"),
        (22, 37, "1400", "ЦО"),
        (22, 38, "1450", "ЦО"),
        (22, 39, "1500", "ЦО"),
        (23, 40, "1550", "ЦО"),
        (23, 41, "1600", "ЦО"),
        (24, 42, "1650", "ЦО"),
        (24, 43, "1700", "ЦО"),
        (25, 44, "1750", "ЦО"),
        (25, 45, "1800", "ЦО"),
    ];

    internal static (string Width, string Type) ResolveDoorInfo(
        double doorOpenTime,
        double doorCloseTime,
        double? doorPreOpening = null)
    {
        int openKey = BuildDoorTimingKey(doorOpenTime);
        int closeKey = BuildDoorTimingKey(doorCloseTime);

        List<(string Width, string Type)> matches = DoorTimingRules
            .Where(rule => rule.OpenKey == openKey && rule.CloseKey == closeKey)
            .Select(rule => (rule.Width, rule.Type))
            .ToList();

        return matches.Count switch
        {
            0 => ("-", "-"),
            1 => matches[0],
            _ => ResolveAmbiguousDoorInfo(matches, doorPreOpening),
        };
    }

    private static int BuildDoorTimingKey(double value)
    {
        return (int)Math.Round(value * 10d, MidpointRounding.AwayFromZero);
    }

    private static (string Width, string Type) ResolveAmbiguousDoorInfo(
        List<(string Width, string Type)> matches,
        double? doorPreOpening)
    {
        if (doorPreOpening is not null)
        {
            string preferredType = doorPreOpening.Value > 0.01d
                ? "ЦО"
                : "ТО";

            (string Width, string Type) exactMatch = matches.FirstOrDefault(match =>
                string.Equals(match.Type, preferredType, StringComparison.Ordinal));

            if (!string.IsNullOrWhiteSpace(exactMatch.Width))
            {
                return exactMatch;
            }
        }

        return ("-", "-");
    }

    private static void SortOneBased(double[] arr)
    {
        if (arr.Length <= 2)
        {
            return;
        }

        Array.Sort(arr, 1, arr.Length - 1);
    }

    private static void ApplyLinest(double[] data, int lastHc5)
    {
        int n = Math.Min(lastHc5, data.Length - 1);
        if (n <= 1)
        {
            return;
        }

        const int p = 5;
        double[,] xtx = new double[p, p];
        double[] xty = new double[p];

        for (int i = 1; i <= n; i++)
        {
            double x = i;
            double x2 = x * x;
            double x3 = x2 * x;
            double x4 = x3 * x;
            double[] row = [x4, x3, x2, x, 1.0];

            for (int r = 0; r < p; r++)
            {
                xty[r] += row[r] * data[i];
                for (int c = 0; c < p; c++)
                {
                    xtx[r, c] += row[r] * row[c];
                }
            }
        }

        if (!SolveLinearSystem(xtx, xty, out double[] coeff))
        {
            return;
        }

        for (int i = 1; i <= n; i++)
        {
            double x = i;
            double x2 = x * x;
            double x3 = x2 * x;
            double x4 = x3 * x;

            double value = coeff[0] * x4 + coeff[1] * x3 + coeff[2] * x2 + coeff[3] * x + coeff[4];
            data[i] = value < 0.1 ? 0 : value;
        }
    }

    private static bool SolveLinearSystem(double[,] matrix, double[] vector, out double[] solution)
    {
        int n = vector.Length;
        solution = new double[n];

        double[,] a = (double[,])matrix.Clone();
        double[] b = (double[])vector.Clone();

        for (int col = 0; col < n; col++)
        {
            int pivotRow = col;
            double pivotValue = Math.Abs(a[col, col]);

            for (int row = col + 1; row < n; row++)
            {
                double candidate = Math.Abs(a[row, col]);
                if (candidate <= pivotValue)
                {
                    continue;
                }

                pivotValue = candidate;
                pivotRow = row;
            }

            if (pivotValue < 1e-9)
            {
                return false;
            }

            if (pivotRow != col)
            {
                SwapRows(a, b, col, pivotRow);
            }

            double pivot = a[col, col];
            for (int j = col; j < n; j++)
            {
                a[col, j] /= pivot;
            }

            b[col] /= pivot;

            for (int row = 0; row < n; row++)
            {
                if (row == col)
                {
                    continue;
                }

                double factor = a[row, col];
                if (Math.Abs(factor) < 1e-12)
                {
                    continue;
                }

                for (int j = col; j < n; j++)
                {
                    a[row, j] -= factor * a[col, j];
                }

                b[row] -= factor * b[col];
            }
        }

        Array.Copy(b, solution, n);
        return true;
    }

    private static void SwapRows(double[,] matrix, double[] vector, int rowA, int rowB)
    {
        int n = vector.Length;
        for (int j = 0; j < n; j++)
        {
            (matrix[rowA, j], matrix[rowB, j]) = (matrix[rowB, j], matrix[rowA, j]);
        }

        (vector[rowA], vector[rowB]) = (vector[rowB], vector[rowA]);
    }

    private static int FindRowByFloorValue(dynamic sheet, double floorValue)
    {
        for (int row = 1; row <= 1000; row++)
        {
            object? cellValue = sheet.Cells(row, 2).Value;
            if (cellValue is null)
            {
                continue;
            }

            if (TryParseFloorCell(cellValue, out double parsed) && NearlyEquals(parsed, floorValue, 0.001))
            {
                return row;
            }
        }

        return 0;
    }

    private static bool TryParseFloorCell(object value, out double parsed)
    {
        parsed = 0;

        if (value is double d)
        {
            parsed = d;
            return true;
        }

        string text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (text.StartsWith('\''))
        {
            text = text[1..];
        }

        if (text.Contains(" - ", StringComparison.Ordinal))
        {
            text = text.Split(" - ", StringSplitOptions.TrimEntries)[0];
        }

        return double.TryParse(text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed);
    }

    private static string FormatFloorForDisplay(double floor)
    {
        string text = floor.ToString("0.###", CultureInfo.InvariantCulture);
        return text.Contains('.', StringComparison.Ordinal)
            ? $"'{text}"
            : text;
    }

    private static string ToShortPercent(double value)
    {
        return NearlyEquals(value, Math.Round(value))
            ? Math.Round(value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static bool IsYes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        return trimmed.Equals("Yes", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals("True", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals("1", StringComparison.Ordinal);
    }

    private static bool ProfilesEqual(string left, string right)
    {
        return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool NearlyEquals(double a, double b, double epsilon = 0.0001)
    {
        return Math.Abs(a - b) <= epsilon;
    }

    private static double AsDouble(object? value)
    {
        return ParseDoubleFlexible(value);
    }

    private static double ToDouble(object? value)
    {
        return ParseDoubleFlexible(value);
    }

    private static int ToInt(object? value)
    {
        return (int)Math.Round(ParseDoubleFlexible(value));
    }

    private static int ToInt(string value)
    {
        return ToInt((object?)value);
    }

    internal static double ParseDoubleFlexible(object? value)
    {
        if (value is null)
        {
            return 0;
        }

        if (value is double d)
        {
            return d;
        }

        if (value is float f)
        {
            return f;
        }

        if (value is decimal m)
        {
            return (double)m;
        }

        if (value is int or long or short or byte)
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        text = text.Trim();
        if (text.StartsWith('\''))
        {
            text = text[1..];
        }

        text = text.Replace(" ", string.Empty, StringComparison.Ordinal)
                   .Replace("%", string.Empty, StringComparison.Ordinal)
                   .Trim();

        return TryParseLocalizedDecimal(text, out double result)
            ? result
            : 0;
    }

    private static double ParseLevelValue(string raw)
    {
        string value = raw;
        if (value.StartsWith("Level ", StringComparison.OrdinalIgnoreCase) && value.Length > 6)
        {
            value = value[6..];
        }

        return ParseDoubleFlexible(value);
    }

    internal static string[] ReadCsvLines(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Encoding encoding = DetectCsvEncoding(bytes);

        using MemoryStream stream = new(bytes);
        using StreamReader reader = new(stream, encoding, detectEncodingFromByteOrderMarks: true);

        List<string> lines = [];
        while (!reader.EndOfStream)
        {
            lines.Add(reader.ReadLine() ?? string.Empty);
        }

        return lines.ToArray();
    }

    private static Encoding DetectCsvEncoding(byte[] bytes)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        if (HasPrefix(bytes, Encoding.UTF8.GetPreamble()))
        {
            return Encoding.UTF8;
        }

        if (HasPrefix(bytes, Encoding.Unicode.GetPreamble()))
        {
            return Encoding.Unicode;
        }

        if (HasPrefix(bytes, Encoding.BigEndianUnicode.GetPreamble()))
        {
            return Encoding.BigEndianUnicode;
        }

        try
        {
            _ = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch
        {
            return Encoding.GetEncoding(1251);
        }
    }

    private static bool HasPrefix(byte[] bytes, byte[] prefix)
    {
        if (prefix.Length == 0 || bytes.Length < prefix.Length)
        {
            return false;
        }

        for (int i = 0; i < prefix.Length; i++)
        {
            if (bytes[i] != prefix[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryParseLocalizedDecimal(string text, out double result)
    {
        result = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string normalized = text.Trim();

        int lastComma = normalized.LastIndexOf(',');
        int lastDot = normalized.LastIndexOf('.');

        if (lastComma >= 0 && lastDot >= 0)
        {
            char decimalSeparator = lastComma > lastDot ? ',' : '.';
            char groupSeparator = decimalSeparator == ',' ? '.' : ',';
            normalized = normalized.Replace(groupSeparator.ToString(), string.Empty, StringComparison.Ordinal);
        }

        if (normalized.Contains(',', StringComparison.Ordinal))
        {
            if (double.TryParse(normalized, NumberStyles.Any, CultureInfo.GetCultureInfo("ru-RU"), out result))
            {
                return true;
            }

            normalized = normalized.Replace(',', '.');
        }

        return double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out result);
    }

    private sealed class MainBatchData
    {
        public string FileName { get; set; } = string.Empty;

        public string SourceFileName { get; set; } = string.Empty;

        public string Folder { get; set; } = string.Empty;

        public double[] AWT { get; set; } = [0];

        public double[] ATTD { get; set; } = [0];
    }

    internal static string? ResolveProjectSourcePath(string fileName, string? sourceFileName, params string?[] searchRoots)
    {
        foreach (string candidate in GetProjectSourceCandidates(fileName, sourceFileName))
        {
            string? resolvedPath = ResolveExistingCsvPath(candidate, searchRoots);
            if (!string.IsNullOrWhiteSpace(resolvedPath))
            {
                return resolvedPath;
            }
        }

        return null;
    }

    internal static string DescribeProjectSourceCandidates(string fileName, string? sourceFileName)
    {
        return string.Join(" or ", GetProjectSourceCandidates(fileName, sourceFileName));
    }

    private static IEnumerable<string> GetProjectSourceCandidates(string fileName, string? sourceFileName)
    {
        yield return $"{fileName}.csv";
        yield return $"{fileName}.elvx";

        if (!string.IsNullOrWhiteSpace(sourceFileName))
        {
            string sourceProjectFileName = Path.GetFileName(sourceFileName);
            if (!sourceProjectFileName.Equals($"{fileName}.elvx", StringComparison.OrdinalIgnoreCase))
            {
                yield return sourceProjectFileName;
            }
        }
    }

    private static string BuildSequentialStem(string sourceStem, int step)
    {
        int endIndex = sourceStem.Length;
        while (endIndex > 0 && char.IsDigit(sourceStem[endIndex - 1]))
        {
            endIndex--;
        }

        string prefix = sourceStem[..endIndex];
        string digits = sourceStem[endIndex..];
        if (digits.Length > 0)
        {
            return prefix + step.ToString($"D{digits.Length}", CultureInfo.InvariantCulture);
        }

        return step == 1
            ? sourceStem
            : $"{sourceStem}{step.ToString(CultureInfo.InvariantCulture)}";
    }

    internal readonly record struct GeneratedReportPaths(string ExcelPath, string PdfPath);

    private sealed class ProjectParsedData
    {
        public ProjectParsedData(
            string[] jobData,
            BuildingDataModel building,
            ElevatorDataModel elevator,
            PassengerDataModel passenger)
        {
            JobData = jobData;
            Building = building;
            Elevator = elevator;
            Passenger = passenger;
        }

        public string[] JobData { get; }

        public BuildingDataModel Building { get; }

        public ElevatorDataModel Elevator { get; }

        public PassengerDataModel Passenger { get; }
    }

    private sealed class BuildingDataModel
    {
        public string BuildingType { get; set; } = string.Empty;

        public double Absenteeism { get; set; }

        public int NoFloors { get; set; }

        public double TotalPeople { get; set; }

        public double CTotalPeople { get; set; }

        public int NoExitFloors { get; set; }

        public double[] FloorName { get; set; } = [0];

        public double[] FloorHeight { get; set; } = [0];

        public double[] FloorLevel { get; set; } = [0];

        public string[] FloorType { get; set; } = [string.Empty];

        public double[] FloorFactor { get; set; } = [0];

        public double[] NoPeople { get; set; } = [0];

        public string[] EntranceFloor { get; set; } = [string.Empty];

        public double[] Bias { get; set; } = [0];
    }

    private sealed class ElevatorDataModel
    {
        public string Dispatcher { get; set; } = string.Empty;

        public int NoElevators { get; set; }

        public string[,] Spec { get; set; } = new string[1, 1];

        public double[] DoorPreOpening { get; set; } = [double.NaN];

        public string[,] FloorsServed { get; set; } = new string[1, 1];
    }

    private sealed class PassengerDataModel
    {
        public double Incoming { get; set; }

        public double Outgoing { get; set; }

        public double Interfloor { get; set; }
    }

    private sealed class CsvSheet
    {
        private readonly List<string[]> rows;

        private CsvSheet(List<string[]> rows, int columnCount)
        {
            this.rows = rows;
            ColumnCount = columnCount;
        }

        public int RowCount => rows.Count;

        public int ColumnCount { get; }

        public static CsvSheet Load(string path)
        {
            string[] lines = ReadCsvLines(path);
            char delimiter = DetectDelimiter(lines);

            List<string[]> rows = new(lines.Length);
            int columnCount = 0;

            foreach (string line in lines)
            {
                string[] parsed = ParseCsvLine(line, delimiter);
                rows.Add(parsed);
                columnCount = Math.Max(columnCount, parsed.Length);
            }

            return new CsvSheet(rows, columnCount);
        }

        public string Get(int row, int column)
        {
            if (row < 1 || row > rows.Count || column < 1)
            {
                return string.Empty;
            }

            string[] rowData = rows[row - 1];
            return column <= rowData.Length
                ? rowData[column - 1]
                : string.Empty;
        }

        public int FindRowExactInColumn(int column, string value)
        {
            for (int row = 1; row <= RowCount; row++)
            {
                if (string.Equals(Get(row, column).Trim(), value, StringComparison.OrdinalIgnoreCase))
                {
                    return row;
                }
            }

            return 0;
        }

        public int FindRowContains(string pattern)
        {
            for (int row = 1; row <= RowCount; row++)
            {
                for (int column = 1; column <= ColumnCount; column++)
                {
                    if (Get(row, column).Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        return row;
                    }
                }
            }

            return 0;
        }

        private static char DetectDelimiter(IEnumerable<string> lines)
        {
            int semicolon = 0;
            int comma = 0;

            foreach (string line in lines.Take(20))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                semicolon += CountCharOutsideQuotes(line, ';');
                comma += CountCharOutsideQuotes(line, ',');
            }

            return semicolon >= comma
                ? ';'
                : ',';
        }

        private static int CountCharOutsideQuotes(string text, char target)
        {
            bool inQuotes = false;
            int count = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < text.Length && text[i + 1] == '"')
                    {
                        i++;
                        continue;
                    }

                    inQuotes = !inQuotes;
                    continue;
                }

                if (!inQuotes && c == target)
                {
                    count++;
                }
            }

            return count;
        }

        private static string[] ParseCsvLine(string line, char delimiter)
        {
            List<string> values = [];
            System.Text.StringBuilder current = new();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }

                    continue;
                }

                if (!inQuotes && c == delimiter)
                {
                    values.Add(current.ToString().Trim());
                    current.Clear();
                    continue;
                }

                current.Append(c);
            }

            values.Add(current.ToString().Trim());
            return values.ToArray();
        }
    }
}
