using ElevateHelperWinUI.Models.Reports;
using ElevateHelperWinUI.Services;
using ElevateHelperWinUI.Services.Reports;
using PdfSharp.Pdf.IO;

namespace ElevateHelper.Tests;

public sealed class ReportPdfRendererTests
{
    [Fact]
    public void InterpolateMetricPoints_AddsExactHalfStepMidpoints()
    {
        ReportMetricPointModel[] simulation =
        [
            new(1, 10, 20, 1, 2, false),
            new(2, 14, 28, 3, 6, false),
        ];

        IReadOnlyList<ReportMetricPointModel> points = ElevateReportService.InterpolateMetricPoints(simulation);

        Assert.Equal(3, points.Count);
        Assert.Equal(1.5, points[1].Hc5);
        Assert.Equal(12, points[1].Wt);
        Assert.Equal(24, points[1].Ttd);
        Assert.Equal(2, points[1].IntermediateStops);
        Assert.Equal(4, points[1].LongWaitPercent);
        Assert.True(points[1].IsInterpolated);
    }

    [Fact]
    public void BuildCappedTimeSeries_StopsAtFirstSimulationAboveMaximum()
    {
        ReportMetricPointModel[] simulation =
        [
            new(1, 35, 110, 1, 2, false),
            new(2, 200, 185, 3, 6, false),
            new(3, 40, 90, 4, 8, false),
        ];
        IReadOnlyList<ReportMetricPointModel> points = ElevateReportService.InterpolateMetricPoints(simulation);

        CappedMetricSeries wt = ReportPdfRenderer.BuildCappedTimeSeries(points, point => point.Wt, 150);
        CappedMetricSeries ttd = ReportPdfRenderer.BuildCappedTimeSeries(points, point => point.Ttd, 150);

        Assert.Equal(2, wt.BoundaryIndex);
        Assert.Equal(new double?[] { 35, 92.5, null, null, null }, wt.Values);
        Assert.Equal(2, ttd.BoundaryIndex);
        Assert.Equal(new double?[] { 110, 130, null, null, null }, ttd.Values);
    }

    [Fact]
    public void BuildCappedTimeSeries_KeepsValuesAtOrBelowMaximum()
    {
        ReportMetricPointModel[] simulation =
        [
            new(1, 150, 120, 1, 2, false),
            new(2, 140, 130, 3, 6, false),
        ];
        IReadOnlyList<ReportMetricPointModel> points = ElevateReportService.InterpolateMetricPoints(simulation);

        CappedMetricSeries series = ReportPdfRenderer.BuildCappedTimeSeries(points, point => point.Wt, 150);

        Assert.Null(series.BoundaryIndex);
        Assert.Equal(new double?[] { 150, 145, 140 }, series.Values);
    }

    [Theory]
    [InlineData(13, "при HC5 13%")]
    [InlineData(6.5, "при HC5 6,5%")]
    public void FormatMetricHc5Caption_MakesSelectedCapacityExplicit(double hc5, string expected)
    {
        Assert.Equal(expected, ReportPdfRenderer.FormatMetricHc5Caption(hc5));
    }

    [Fact]
    public void GroupTrafficRows_SumsExactSharesBeforeRounding()
    {
        const double exactFloorShare = 100d / 1300d * 100d;
        List<ReportTrafficFloorModel> floors =
        [
            new("1", 1, "0", "0", "100% ↑", "—", "—", 100d),
            .. Enumerable.Range(2, 13).Select(floor => new ReportTrafficFloorModel(
                floor.ToString(), 1, "100", "0,8", "7,7% ↓", "—", "—", exactFloorShare)),
        ];

        IReadOnlyList<ReportTrafficFloorModel> grouped = ReportPdfRenderer.GroupTrafficRows(floors);

        Assert.Equal(2, grouped.Count);
        Assert.Equal("2–14", grouped[1].Floor);
        Assert.Equal(13, grouped[1].FloorCount);
        Assert.Equal("100,0% ↓", grouped[1].Incoming);
    }

    [Fact]
    public void GroupTrafficRows_AggregatesEntranceAndDestinationRangesIndependently()
    {
        const double destinationShare = 155d / 3863d * 100d;
        List<ReportTrafficFloorModel> floors =
        [
            new("−2", 1, "149", "1,2", "5% ↑", "—", "—", 5d),
            new("−1", 1, "149", "1,2", "5% ↑", "—", "—", 5d),
            new("1", 1, "0", "0", "90% ↑", "—", "—", 90d),
            .. Enumerable.Range(2, 23).Select(floor => new ReportTrafficFloorModel(
                floor.ToString(), 1, "155", "0,8", "4,0% ↓", "—", "—", destinationShare)),
        ];

        IReadOnlyList<ReportTrafficFloorModel> grouped = ReportPdfRenderer.GroupTrafficRows(floors);

        Assert.Equal(3, grouped.Count);
        Assert.Equal("−2–−1", grouped[0].Floor);
        Assert.Equal("10,0% ↑", grouped[0].Incoming);
        Assert.Equal("2–24", grouped[2].Floor);
        Assert.Equal("92,3% ↓", grouped[2].Incoming);
    }

    [Fact]
    public void LiftConfigurationSummary_AccountsForEveryLift()
    {
        ReportDocumentModel model = CreateModel(floorCount: 14, elevatorCount: 7);

        Assert.Equal(2, ReportLiftConfiguration.CountDistinct(model.LiftGroup.Lifts));
        Assert.Equal("7 лифтов · 2 конфигурации", ElevateReportService.BuildCompactElevatorSummary(model.LiftGroup.Lifts));
        Assert.Equal("1600 / 2000 кг",
            ReportPdfRenderer.SummarizeLiftValues(model.LiftGroup.Lifts, lift => lift.CapacityKg, "кг"));
    }

    [Fact]
    public void FitLogoDestination_PreservesVisibleLogoAspectRatio()
    {
        var destination = ReportPdfRenderer.FitLogoDestination(32, 28, 106, 35);

        Assert.Equal(278.25 / 94.25, destination.Width / destination.Height, 10);
        Assert.True(destination.Width <= 106);
        Assert.True(destination.Height <= 35);
    }

    [Fact]
    public void Generate_FigmaSizedScenario_CreatesSixA4Pages()
    {
        ReportDocumentModel model = CreateModel(floorCount: 14, elevatorCount: 7);
        string pdfPath = Generate(model);

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        Assert.Equal(6, document.PageCount);
        Assert.All(document.Pages.Cast<PdfSharp.Pdf.PdfPage>(), page =>
        {
            Assert.InRange(page.Width.Point, 594.9, 595.1);
            Assert.InRange(page.Height.Point, 841.9, 842.1);
        });
    }

    [Fact]
    public void Generate_MinimalScenario_DoesNotAddEmptyContinuationPages()
    {
        ReportDocumentModel model = CreateModel(floorCount: 1, elevatorCount: 1);
        string pdfPath = Generate(model);

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        Assert.Equal(6, document.PageCount);
    }

    [Fact]
    public void Generate_LargeScenario_PaginatesTablesAndRepeatsPageChrome()
    {
        ReportDocumentModel model = CreateModel(floorCount: 80, elevatorCount: 9);
        string pdfPath = Generate(model);

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        // Identical consecutive service and traffic rows are intentionally collapsed
        // into ranges, while the building table still paginates every floor.
        Assert.True(document.PageCount >= 9);
        Assert.All(document.Pages.Cast<PdfSharp.Pdf.PdfPage>(), page =>
        {
            Assert.InRange(page.Width.Point, 594.9, 595.1);
            Assert.InRange(page.Height.Point, 841.9, 842.1);
        });
    }

    [Fact]
    public void Generate_LongTextScenario_CompletesWithoutLayoutFailure()
    {
        string longName = string.Join(' ', Enumerable.Repeat("Многофункциональный административно-гостиничный комплекс", 8));
        ReportDocumentModel original = CreateModel(floorCount: 18, elevatorCount: 4);
        ReportDocumentModel model = original with
        {
            Metadata = original.Metadata with
            {
                ProjectName = longName,
                AddressOrCalculation = longName,
                Author = longName,
            },
        };
        string pdfPath = Generate(model);

        Assert.True(new FileInfo(pdfPath).Length > 20_000);
        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        Assert.True(document.PageCount >= 6);
    }

    [Fact]
    public void Generate_TimeSeriesAbove150_CreatesClippedAssessmentPreview()
    {
        ReportDocumentModel original = CreateModel(floorCount: 14, elevatorCount: 7);
        double[] wt = [12, 14, 16, 18, 20, 22, 24, 27, 30, 33, 35, 200, 220];
        double[] ttd = [48, 52, 57, 62, 68, 75, 83, 92, 101, 110, 185, 205, 225];
        List<ReportMetricPointModel> simulation = Enumerable.Range(1, 13)
            .Select(index => new ReportMetricPointModel(index, wt[index - 1], ttd[index - 1], index * 0.2, index * 0.1, false))
            .ToList();
        IReadOnlyList<ReportMetricPointModel> display = ElevateReportService.InterpolateMetricPoints(simulation);
        ReportDocumentModel model = original with
        {
            Assessment = original.Assessment with
            {
                SimulationPoints = simulation,
                DisplayPoints = display,
                TargetWt = 60,
                ActiveThreshold = original.Assessment.ActiveThreshold with { TtdSeconds = 120 },
            },
        };
        string pdfPath = Path.Combine(Path.GetTempPath(), $"elevate-report-capped-time-series-net{Environment.Version.Major}.pdf");

        using (ReportPdfRenderer renderer = new(model, FindAssetRoot()))
        {
            renderer.Generate(pdfPath);
        }

        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        Assert.Equal(6, document.PageCount);
        Assert.True(new FileInfo(pdfPath).Length > 20_000);
    }

    private static string Generate(ReportDocumentModel model)
    {
        string output = Path.Combine(Path.GetTempPath(), $"elevate-report-{Guid.NewGuid():N}.pdf");
        using ReportPdfRenderer renderer = new(model, FindAssetRoot());
        renderer.Generate(output);
        return output;
    }

    internal static string FindAssetRoot()
    {
        string[] candidates =
        [
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Reports"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Assets/Reports")),
        ];
        return candidates.First(path => File.Exists(Path.Combine(path, "Fonts", "Geologica-Regular.ttf")));
    }

    internal static ReportDocumentModel CreateModel(int floorCount, int elevatorCount)
    {
        int simulationCount = 13;
        List<ReportMetricPointModel> simulation = Enumerable.Range(1, simulationCount)
            .Select(index => new ReportMetricPointModel(index, 3 + index * 2.4, 24 + index * 3.1, index * 0.2, index * 0.1, false))
            .ToList();
        IReadOnlyList<ReportMetricPointModel> display = ElevateReportService.InterpolateMetricPoints(simulation);
        List<ReportLiftModel> lifts = Enumerable.Range(1, elevatorCount)
            .Select(index => index == elevatorCount && elevatorCount > 1
                ? new ReportLiftModel(index, "2000", 4.20, "3,0", "1,0", "1,2", "0,6", "1200", "ЦО",
                    "0,5", "2,0", "3,3", "2,2")
                : new ReportLiftModel(index, "1600", 3.36, "2,5", "0,9", "1,0", "0,5", "1100", "ЦО",
                    "0,5", "1,9", "3,1", "2,0"))
            .ToList();
        List<ReportFloorServiceModel> service = Enumerable.Range(1, floorCount)
            .Select(floor => new ReportFloorServiceModel(floor.ToString(), Enumerable.Repeat(true, elevatorCount).ToArray()))
            .ToList();
        List<ReportBuildingFloorModel> buildingFloors = Enumerable.Range(1, floorCount)
            .Select(floor => new ReportBuildingFloorModel(floor.ToString(), 4.2, (floor - 1) * 4.2, floor == 1 ? "Лобби" : "Офис",
                floor == 1 ? 0 : 100, floor == 1 ? 0 : 0.8, floor == 1 ? 0 : 80))
            .ToList();
        List<ReportTrafficFloorModel> trafficFloors = Enumerable.Range(1, floorCount)
            .Select(floor =>
            {
                double exactShare = 100d / Math.Max(100, (floorCount - 1) * 100) * 100d;
                return new ReportTrafficFloorModel(
                    floor.ToString(),
                    1,
                    floor == 1 ? "0" : "100",
                    floor == 1 ? "0" : "0,8",
                    floor == 1 ? "100% ↑" : $"{exactShare.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))}% ↓",
                    "—",
                    "—",
                    floor == 1 ? 100d : exactShare);
            })
            .ToList();
        ReportCriteriaProfileModel morning = new("Офис · 100 / 0 / 0", new(11, 40, 120), new(12, 30, 100), new(13, 25, 80));
        ReportCriteriaProfileModel lunch = new("Офис · 45 / 45 / 10", new(10, 40, 120), new(11, 40, 100), new(12, 25, 80));
        ReportCriteriaProfileModel hotel = new("Гостиница · 50 / 50 / 0", new(11, 40, 120), new(12, 40, 100), new(13, 25, 80));
        ReportCriteriaProfileModel residential = new("Жильё · 50 / 50 / 0", new(6, 60, 150), new(7, 60, 120), new(8, 40, 90));

        return new ReportDocumentModel(
            new ReportMetadataModel("Б. Тульская 10 БЦ · Группа 1 · утренний пик", "г. Москва, ул. Большая Тульская, 10", "R13",
                "Лесничий", new DateTime(2026, 8, 20), "Офисное здание", "Симуляционное моделирование"),
            new ReportAssessmentModel(5, "100 / 0 / 0", ElevateReportService.BuildCompactElevatorSummary(lifts), simulationCount, 30,
                simulation, display, new ReportAssessmentResultModel(13, simulation[^1].Wt, simulation[^1].Ttd,
                    simulation[^1].IntermediateStops, simulation[^1].LongWaitPercent, 5), morning.FiveStars),
            new ReportLiftGroupModel("На этаж назначения (DDS)", $"{floorCount} / {floorCount}", lifts, service),
            new ReportBuildingModel(floorCount, Math.Max(0, floorCount - 1), Math.Max(0, floorCount - 1) * 80,
                "0,8", $"{floorCount} / {floorCount}", buildingFloors),
            new ReportTrafficModel(100, 0, 0, simulationCount, display.Count, trafficFloors),
            new ReportCriteriaModel(morning.Name, [morning, lunch, hotel, residential],
                "Тип пассажиропотока (100 / 0 / 0)",
                "Результаты отчета действительны при исследовании теоретических сценариев движения вертикального транспорта с использованием оборудования, сервисов и инструментов планирования пассажиропотока MLT. Результаты отчета зависят от значений параметров работы оборудования, используемых в качестве исходных данных, и применимы только совместно с ними.\n\nРезультаты не следует интерпретировать как заявление или гарантию работоспособности какой-либо фактической системы вертикального транспорта. MLT ни при каких обстоятельствах не несёт ответственности за любой ущерб, причиненный или понесенный в связи с использованием результатов отчета. Запрещено копировать, воспроизводить или изменять результаты отчета, а также передавать их третьим лицам."));
    }
}
