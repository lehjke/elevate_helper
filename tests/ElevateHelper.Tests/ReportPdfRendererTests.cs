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
            .Select(index => new ReportLiftModel(index, "1600", 3.36, "2,5", "0,9", "1,0", "0,5", "1100", "ЦО",
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
            .Select(floor => new ReportTrafficFloorModel(floor.ToString(), 1, floor == 1 ? "0" : "100", floor == 1 ? "0" : "0,8",
                floor == 1 ? "100% ↑" : "по доле ↓", "—", "—"))
            .ToList();
        ReportCriteriaProfileModel morning = new("Офис · 100 / 0 / 0", new(11, 40, 120), new(12, 30, 100), new(13, 25, 80));
        ReportCriteriaProfileModel lunch = new("Офис · 45 / 45 / 10", new(10, 40, 120), new(11, 40, 100), new(12, 25, 80));
        ReportCriteriaProfileModel hotel = new("Гостиница · 50 / 50 / 0", new(11, 40, 120), new(12, 40, 100), new(13, 25, 80));
        ReportCriteriaProfileModel residential = new("Жильё · 50 / 50 / 0", new(6, 60, 150), new(7, 60, 120), new(8, 40, 90));

        return new ReportDocumentModel(
            new ReportMetadataModel("Б. Тульская 10 БЦ · Группа 1 · утренний пик", "г. Москва, ул. Большая Тульская, 10", "R13",
                "Лесничий", new DateTime(2026, 8, 20), "Офисное здание", "Симуляционное моделирование"),
            new ReportAssessmentModel(5, "100 / 0 / 0", $"{elevatorCount} × 1600 кг · 2,5 м/с", simulationCount, 30,
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
