using System.Globalization;
using ElevateHelperWinUI.Models.Reports;
using PdfSharp.Drawing;
using PdfSharp.Drawing.Layout;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace ElevateHelperWinUI.Services.Reports;

internal sealed class ReportPdfRenderer : IDisposable
{
    internal const double PageWidth = 595;
    internal const double PageHeight = 842;
    internal const double PageLeft = 32;
    internal const double PageRight = 32;
    internal const double ContentWidth = PageWidth - PageLeft - PageRight;
    internal const double ContentTop = 92;
    internal const double ContentBottom = 786;

    private static readonly object FontLock = new();
    private static ReportFontResolver? configuredFontResolver;

    private static readonly XColor Gold = XColor.FromArgb(0xD7, 0xB2, 0x85);
    private static readonly XColor Gold10 = XColor.FromArgb(0xF8, 0xF2, 0xEB);
    private static readonly XColor Blue = XColor.FromArgb(0x15, 0x2F, 0x60);
    private static readonly XColor Blue40 = XColor.FromArgb(0x89, 0x8F, 0xAF);
    private static readonly XColor Blue20 = XColor.FromArgb(0xBA, 0xBF, 0xD2);
    private static readonly XColor Deep = XColor.FromArgb(0x10, 0x1A, 0x38);
    private static readonly XColor Deep60 = XColor.FromArgb(0x5D, 0x64, 0x7E);
    private static readonly XColor Deep20 = XColor.FromArgb(0xBB, 0xBE, 0xCD);
    private static readonly XColor Deep10 = XColor.FromArgb(0xD7, 0xDA, 0xE3);
    private static readonly XColor Line = XColor.FromArgb(0xDF, 0xE2, 0xE9);
    private static readonly XColor Panel = XColor.FromArgb(0xFA, 0xFB, 0xFC);
    private static readonly XRect VisibleLogoSource = new(11.75, 10.75, 278.25, 94.25);

    private readonly ReportDocumentModel model;
    private readonly XImage logo;
    private readonly List<PagePlan> pages = [];

    public ReportPdfRenderer(ReportDocumentModel model, string assetRoot)
    {
        this.model = model;
        ConfigureFonts(Path.Combine(assetRoot, "Fonts"));
        logo = XImage.FromFile(Path.Combine(assetRoot, "Images", "MLT_logo_RU_W_Lifts_W@4x.png"));
    }

    public void Generate(string outputPath)
    {
        pages.Clear();
        BuildPagePlan();

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException($"Cannot resolve PDF output directory for {outputPath}."));

        using PdfDocument document = new();
        document.Info.Title = $"Анализ пассажиропотока · {model.Metadata.ProjectName}";
        document.Info.Author = "MLT";
        document.Info.Subject = "Симуляционное моделирование пассажиропотока";

        for (int index = 0; index < pages.Count; index++)
        {
            PagePlan plan = pages[index];
            PdfPage page = document.AddPage();
            page.Width = XUnit.FromPoint(PageWidth);
            page.Height = XUnit.FromPoint(PageHeight);

            using XGraphics graphics = XGraphics.FromPdfPage(page);
            graphics.DrawRectangle(XBrushes.White, 0, 0, PageWidth, PageHeight);
            if (!plan.IsCover)
            {
                DrawHeader(graphics, index + 1, pages.Count);
                DrawFooter(graphics);
            }

            plan.Draw(graphics);
        }

        document.Save(outputPath);
    }

    private static void ConfigureFonts(string fontDirectory)
    {
        lock (FontLock)
        {
            configuredFontResolver ??= new ReportFontResolver(fontDirectory);
            if (GlobalFontSettings.FontResolver is null)
            {
                GlobalFontSettings.FontResolver = configuredFontResolver;
            }
        }
    }

    private void BuildPagePlan()
    {
        pages.Add(new PagePlan(true, DrawCover));
        pages.Add(new PagePlan(false, DrawAssessment));
        BuildLiftPagePlans();
        BuildBuildingPagePlans();
        BuildTrafficPagePlans();
        pages.Add(new PagePlan(false, DrawCriteria));
    }

    private void DrawHeader(XGraphics graphics, int pageNumber, int totalPages)
    {
        const double y = 28;
        DrawLogo(graphics, 32, 28, 106, 35);
        DrawText(
            graphics,
            $"Анализ пассажиропотока · {model.Metadata.BuildingTypeLabel}\n{model.Metadata.ProjectName} / {model.Metadata.Revision}",
            Regular(7.5),
            Deep60,
            new XRect(170, y + 8, 320, 24),
            XParagraphAlignment.Left,
            11);
        DrawString(
            graphics,
            $"{pageNumber:00} / {totalPages:00}",
            Regular(8),
            Deep60,
            new XRect(500, y + 12, 63, 12),
            XStringFormats.TopRight);
        graphics.DrawLine(new XPen(Deep10, 1), PageLeft, y + 42, PageWidth - PageRight, y + 42);
    }

    private void DrawFooter(XGraphics graphics)
    {
        const double y = 792;
        graphics.DrawLine(new XPen(Deep10, 1), PageLeft, y, PageWidth - PageRight, y);
        DrawString(graphics, "MLT · Анализ пассажиропотока", Regular(7), Deep60,
            new XRect(PageLeft, y + 9, 240, 10), XStringFormats.TopLeft);
        DrawString(graphics, $"{model.Metadata.ProjectName} · {model.Metadata.Revision}", Regular(7), Deep60,
            new XRect(270, y + 9, PageWidth - PageRight - 270, 10), XStringFormats.TopRight);
    }

    private void DrawCover(XGraphics graphics)
    {
        DrawLogo(graphics, 38, 39, 149, 50);
        DrawText(
            graphics,
            $"Отчёт {model.Metadata.Revision}\nВерсия 01 · {model.Metadata.GeneratedAt:dd.MM.yyyy}",
            Regular(8), Deep60, new XRect(390, 51, 167, 30), XParagraphAlignment.Right, 12);

        graphics.DrawRectangle(new XSolidBrush(Gold), 38, 266.5, 4, 230);
        DrawText(graphics, "Анализ\nпассажиро-\nпотока", ExtraBold(37), Deep,
            new XRect(60, 263.5, 245, 123), XParagraphAlignment.Left, 41);
        DrawText(graphics, model.Metadata.BuildingTypeLabel, SemiBold(17), Blue,
            new XRect(60, 405.5, 245, 22), XParagraphAlignment.Left, 22, maxLines: 1);
        DrawText(
            graphics,
            "Оценка качества вертикального транспорта на основе параметров здания, лифтовой группы и сценария движения.",
            Regular(9), Deep60, new XRect(60, 453.5, 245, 41), XParagraphAlignment.Left, 15);

        DrawTowerGraphic(graphics, 338.75, 208.5, 218.25, 346);
        DrawCoverMetadata(graphics);
    }

    private void DrawTowerGraphic(XGraphics graphics, double x, double y, double width, double height)
    {
        double baseY = y + height;
        graphics.DrawLine(new XPen(Deep, 2), x, baseY, x + width, baseY);

        DrawTower(graphics, x + 18, baseY - 226, 72, 224, false);
        DrawTower(graphics, x + 82, baseY - 316, 94, 314, true);
        DrawTower(graphics, x + 146.25, baseY - 170, 66, 168, false);
        graphics.DrawLine(new XPen(Gold, 1), x + 1, baseY - 43, x + width, baseY - 43);
    }

    private static void DrawTower(XGraphics graphics, double x, double y, double width, double height, bool goldTop)
    {
        graphics.DrawRectangle(new XPen(Blue, 1), XBrushes.White, x, y, width, height);
        if (goldTop)
        {
            graphics.DrawRectangle(new XSolidBrush(Gold), x, y, width, 8);
        }

        for (double gridY = y + 12; gridY < y + height - 6; gridY += 18)
        {
            graphics.DrawLine(new XPen(Deep10, 0.75), x + 10, gridY, x + width - 10, gridY);
        }

        for (double gridX = x + 10; gridX < x + width - 6; gridX += 18)
        {
            graphics.DrawLine(new XPen(Deep10, 0.75), gridX, y + 12, gridX, y + height - 10);
        }
    }

    private void DrawCoverMetadata(XGraphics graphics)
    {
        const double x = 38;
        const double y = 661;
        const double width = 519;
        const double colWidth = width / 2;
        const double rowHeight = 50;
        (string Label, string Value)[] values =
        [
            ("Объект", model.Metadata.ProjectName),
            ("Адрес / описание расчёта", model.Metadata.AddressOrCalculation),
            ("Версия отчёта", model.Metadata.Revision),
            ("Исполнитель", model.Metadata.Author),
            ("Дата", model.Metadata.GeneratedAt.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)),
            ("Метод", model.Metadata.MethodLabel),
        ];

        graphics.DrawLine(new XPen(Deep10, 1), x, y, x + width, y);
        graphics.DrawLine(new XPen(Deep10, 1), x + colWidth, y, x + colWidth, y + rowHeight * 3);

        for (int index = 0; index < values.Length; index++)
        {
            int row = index / 2;
            int column = index % 2;
            double cellX = x + column * colWidth + (column == 0 ? 0 : 18);
            double cellWidth = colWidth - (column == 0 ? 18 : 0);
            double cellY = y + row * rowHeight;
            graphics.DrawLine(new XPen(Deep10, 1), x, cellY + rowHeight, x + width, cellY + rowHeight);
            DrawString(graphics, values[index].Label, Regular(7), Deep60,
                new XRect(cellX, cellY + 12, cellWidth, 10), XStringFormats.TopLeft);
            DrawText(graphics, values[index].Value, SemiBold(9), Deep,
                new XRect(cellX, cellY + 27, cellWidth, 13), XParagraphAlignment.Left, 13, maxLines: 1);
        }
    }

    private void DrawAssessment(XGraphics graphics)
    {
        DrawPageTitle(graphics, "Качество обслуживания", "Итоговые показатели моделирования и соответствие критериям");
        DrawAssessmentSummary(graphics, 154);

        const double chartY = 243;
        const double chartWidth = 260.5;
        const double chartHeight = 184;
        DrawMetricChart(graphics, PageLeft, chartY, chartWidth, chartHeight, "WT · среднее ожидание",
            model.Assessment.Result.Wt, "с", model.Assessment.TargetWt is double target ? $"Target WT {Format(target)} с" : string.Empty,
            point => point.Wt, model.Assessment.TargetWt, "с", 150, "Цель WT");
        DrawMetricChart(graphics, PageLeft + chartWidth + 10, chartY, chartWidth, chartHeight, "TTD · время до назначения",
            model.Assessment.Result.Ttd, "с", $"порог категории < {model.Assessment.ActiveThreshold.TtdSeconds} с",
            point => point.Ttd, model.Assessment.ActiveThreshold.TtdSeconds, "с", 150, "Порог TTD");
        DrawMetricChart(graphics, PageLeft, chartY + chartHeight + 10, chartWidth, chartHeight, "IS · промежуточные остановки",
            model.Assessment.Result.IntermediateStops, string.Empty, "справочный показатель",
            point => point.IntermediateStops, null, "ост.", null, string.Empty);
        DrawMetricChart(graphics, PageLeft + chartWidth + 10, chartY + chartHeight + 10, chartWidth, chartHeight, "LW · ожидание ≥ 90 с",
            model.Assessment.Result.LongWaitPercent, "%", "чем ниже, тем лучше",
            point => point.LongWaitPercent, null, "%", null, string.Empty);

        DrawHc5Strip(graphics, PageLeft, 630, ContentWidth, model.Assessment.DisplayPoints);
        DrawAssessmentResultTable(graphics, PageLeft, 683, ContentWidth);
    }

    private void DrawPageTitle(XGraphics graphics, string title, string subtitle)
    {
        DrawString(graphics, title, ExtraBold(22), Deep, new XRect(PageLeft, ContentTop, ContentWidth, 27), XStringFormats.TopLeft);
        DrawText(graphics, subtitle, Regular(8), Deep60, new XRect(PageLeft, ContentTop + 32, ContentWidth, 24),
            XParagraphAlignment.Left, 12, maxLines: 2);
    }

    private void DrawAssessmentSummary(XGraphics graphics, double y)
    {
        string[] elevatorParts = model.Assessment.ElevatorSummary.Split('·', 2, StringSplitOptions.TrimEntries);
        string elevatorValue = elevatorParts[0];
        string elevatorSub = elevatorParts.Length > 1
            ? $"{elevatorParts[1]} · остановки {model.LiftGroup.ServedFloorSummary}"
            : model.LiftGroup.ServedFloorSummary;
        double[] widths = [112, (ContentWidth - 112) / 2, (ContentWidth - 112) / 2];
        string[] labels = ["Итоговая оценка", "Сценарий потока", "Лифтовая группа"];
        string[] values = [$"{model.Assessment.Rating} / 5", model.Assessment.TrafficProfile, elevatorValue];
        string[] sub = [string.Empty, "входящий / выходящий / межэтажный", elevatorSub];
        double x = PageLeft;

        for (int index = 0; index < widths.Length; index++)
        {
            XBrush fill = index == 0 ? new XSolidBrush(Deep) : XBrushes.White;
            graphics.DrawRectangle(new XPen(Line, 1), fill, x, y, widths[index], 77);
            XColor labelColor = index == 0 ? Deep20 : Deep60;
            XColor valueColor = index == 0 ? XColors.White : Deep;
            DrawString(graphics, labels[index], Regular(7), labelColor, new XRect(x + 14, y + 13, widths[index] - 28, 10), XStringFormats.TopLeft);
            DrawText(graphics, values[index], SemiBold(18), valueColor,
                new XRect(x + 14, y + 29, widths[index] - 28, 23), XParagraphAlignment.Left, 21, maxLines: 1);
            DrawText(graphics, sub[index], Regular(7), labelColor,
                new XRect(x + 14, y + 54, widths[index] - 28, 12), XParagraphAlignment.Left, 10, maxLines: 1);
            if (index == 0)
            {
                DrawStars(graphics, x + 14, y + 55, model.Assessment.Rating, 5.2, Deep20, Deep60);
            }
            x += widths[index];
        }
    }

    private void DrawMetricChart(
        XGraphics graphics,
        double x,
        double y,
        double width,
        double height,
        string name,
        double result,
        string resultUnit,
        string note,
        Func<ReportMetricPointModel, double> valueSelector,
        double? target,
        string axisUnit,
        double? graphMaximum,
        string targetLegend)
    {
        graphics.DrawRoundedRectangle(new XPen(Line, 1), XBrushes.White, x, y, width, height, 4, 4);
        DrawString(graphics, name, SemiBold(9), Deep, new XRect(x + 11, y + 10, width * 0.62, 13), XStringFormats.TopLeft);
        DrawString(graphics, $"{Format(result)} {resultUnit}".Trim(), SemiBold(15), Deep,
            new XRect(x + width * 0.56, y + 9, width * 0.40 - 11, 18), XStringFormats.TopRight);
        DrawString(graphics, note, Regular(6.5), Deep60,
            new XRect(x + 11, y + 29, width * 0.54 - 11, 10), XStringFormats.TopLeft);
        DrawString(graphics, FormatMetricHc5Caption(model.Assessment.Result.Hc5), SemiBold(6.5), Blue,
            new XRect(x + width * 0.54, y + 29, width * 0.42 - 11, 10), XStringFormats.TopRight);
        DrawString(graphics, "Провозная способность HC5, %", Regular(6.8), Deep,
            new XRect(x + 11, y + 42, width - 22, 10), XStringFormats.TopRight);

        IReadOnlyList<ReportMetricPointModel> points = model.Assessment.DisplayPoints;
        CappedMetricSeries? cappedSeries = graphMaximum is double maximum
            ? BuildCappedTimeSeries(points, valueSelector, maximum)
            : null;
        IReadOnlyList<double?> plotValues = cappedSeries?.Values
            ?? points.Select(point => (double?)valueSelector(point)).ToArray();
        double maxValue = Math.Max(plotValues.Where(value => value.HasValue).Select(value => value!.Value).DefaultIfEmpty(0).Max(), target ?? 0);
        AxisScale scale = graphMaximum is double fixedMaximum
            ? BuildFixedAxisScale(fixedMaximum, 5)
            : BuildAxisScale(maxValue * 1.18);
        double plotX = x + 44.25;
        double plotY = y + 59.408;
        double plotWidth = 196.725;
        double plotHeight = 75.674;

        for (int i = 0; i < scale.Values.Count; i++)
        {
            double value = scale.Values[i];
            double gridY = plotY + plotHeight - value / scale.Maximum * plotHeight;
            graphics.DrawLine(new XPen(Deep10, 0.75), plotX, gridY, plotX + plotWidth, gridY);
            DrawString(graphics, AxisLabel(value, axisUnit), Regular(3.8), Deep60,
                new XRect(x + 3, gridY - 3, 37, 8), XStringFormats.TopRight);
        }

        for (int i = 0; i < points.Count; i++)
        {
            double pointX = plotX + plotWidth * i / Math.Max(points.Count - 1, 1);
            graphics.DrawLine(new XPen(points[i].IsInterpolated ? XColor.FromArgb(0xE8, 0xEA, 0xF0) : Deep10,
                points[i].IsInterpolated ? 0.4 : 0.55), pointX, plotY, pointX, plotY + plotHeight);
            graphics.DrawLine(new XPen(Blue40, 0.7), pointX, plotY + plotHeight, pointX,
                plotY + plotHeight + (points[i].IsInterpolated ? 1.7 : 2.5));
            DrawString(graphics, FormatHc5(points[i].Hc5), Regular(points.Count > 15 ? 3.7 : 4.6), Deep60,
                new XRect(pointX - 8, plotY + plotHeight + 5, 16, 7), XStringFormats.TopCenter);
        }

        if (target is double targetValue)
        {
            double targetY = plotY + plotHeight - Math.Min(targetValue, scale.Maximum) / scale.Maximum * plotHeight;
            graphics.DrawLine(new XPen(Gold, 1.8), plotX, targetY, plotX + plotWidth, targetY);
        }

        List<XPoint> linePoints = new(points.Count + 1);
        for (int i = 0; i < points.Count; i++)
        {
            if (plotValues[i] is not double value)
            {
                continue;
            }

            double pointX = plotX + plotWidth * i / Math.Max(points.Count - 1, 1);
            double pointY = plotY + plotHeight - Math.Min(value, scale.Maximum) / scale.Maximum * plotHeight;
            linePoints.Add(new XPoint(pointX, pointY));
        }

        if (linePoints.Count > 1)
        {
            graphics.DrawLines(new XPen(Blue, 2), linePoints.ToArray());
        }

        if (cappedSeries?.BoundaryIndex is int boundaryIndex && linePoints.Count > 0)
        {
            double boundaryX = plotX + plotWidth * boundaryIndex / Math.Max(points.Count - 1, 1);
            XPen clippedSegmentPen = new(Blue, 2) { DashStyle = XDashStyle.Dash };
            graphics.DrawLine(clippedSegmentPen, linePoints[^1], new XPoint(boundaryX, plotY));
        }

        for (int i = 0; i < points.Count; i++)
        {
            if (plotValues[i] is not double value)
            {
                continue;
            }

            double pointX = plotX + plotWidth * i / Math.Max(points.Count - 1, 1);
            double pointY = plotY + plotHeight - Math.Min(value, scale.Maximum) / scale.Maximum * plotHeight;
            XPoint point = new(pointX, pointY);
            if (points[i].IsInterpolated)
            {
                graphics.DrawEllipse(new XPen(Gold, 1.05), XBrushes.White, point.X - 1.45, point.Y - 1.45, 2.9, 2.9);
            }
            else
            {
                graphics.DrawEllipse(new XSolidBrush(Blue), point.X - 2.1, point.Y - 2.1, 4.2, 4.2);
            }
        }

        double legendY = y + 163;
        if (target.HasValue)
        {
            graphics.DrawEllipse(new XSolidBrush(Blue), x + 12, legendY, 6, 6);
            DrawString(graphics, "Моделирование · 1%", Regular(5.2), Deep60,
                new XRect(x + 23, legendY - 1, 68, 8), XStringFormats.TopLeft);
            graphics.DrawEllipse(new XPen(Gold, 1.2), XBrushes.White, x + 95, legendY, 6, 6);
            DrawString(graphics, "Интерполяция · 0,5%", Regular(5.2), Deep60,
                new XRect(x + 106, legendY - 1, 84, 8), XStringFormats.TopLeft);
            XFont targetLegendFont = Regular(5.2);
            double targetTextWidth = graphics.MeasureString(targetLegend, targetLegendFont).Width;
            double targetTextRight = x + width - 9;
            double targetTextX = targetTextRight - targetTextWidth;
            double targetLineEnd = targetTextX - 4;
            graphics.DrawLine(new XPen(Gold, 2), targetLineEnd - 14, legendY + 3, targetLineEnd, legendY + 3);
            DrawString(graphics, targetLegend, targetLegendFont, Deep60,
                new XRect(targetTextX, legendY - 1, targetTextWidth + 0.5, 8), XStringFormats.TopLeft);
        }
        else
        {
            graphics.DrawEllipse(new XSolidBrush(Blue), x + 12, legendY, 6, 6);
            DrawString(graphics, "Моделирование · 1%", Regular(5.6), Deep60,
                new XRect(x + 23, legendY - 1, 80, 8), XStringFormats.TopLeft);
            graphics.DrawEllipse(new XPen(Gold, 1.2), XBrushes.White, x + 110, legendY, 6, 6);
            DrawString(graphics, "Интерполяция · 0,5%", Regular(5.6), Deep60,
                new XRect(x + 121, legendY - 1, 92, 8), XStringFormats.TopLeft);
        }
    }

    private void DrawHc5Strip(XGraphics graphics, double x, double y, double width, IReadOnlyList<ReportMetricPointModel> points)
    {
        const double labelWidth = 48;
        const double rowHeight = 21;
        double cellWidth = (width - labelWidth) / Math.Max(points.Count, 1);
        DrawTableCell(graphics, x, y, labelWidth, rowHeight, "HC5, %", SemiBold(6.6), XColors.White, Blue, XParagraphAlignment.Center);
        DrawTableCell(graphics, x, y + rowHeight, labelWidth, rowHeight, "WT, с", SemiBold(6.6), XColors.White, Deep, XParagraphAlignment.Center);
        CappedMetricSeries wtSeries = BuildCappedTimeSeries(points, point => point.Wt, 150);

        for (int i = 0; i < points.Count; i++)
        {
            double cellX = x + labelWidth + i * cellWidth;
            XColor fill = points[i].IsInterpolated ? Gold10 : XColors.White;
            XColor textColor = points[i].IsInterpolated ? Blue : Deep60;
            DrawTableCell(graphics, cellX, y, cellWidth, rowHeight, FormatHc5(points[i].Hc5), Regular(6.6), textColor, fill, XParagraphAlignment.Center);
            string wtValue = wtSeries.Values[i] is double value ? Format(value, 1) : "—";
            DrawTableCell(graphics, cellX, y + rowHeight, cellWidth, rowHeight, wtValue, SemiBold(6.6), Deep, fill, XParagraphAlignment.Center);
        }
    }

    internal static CappedMetricSeries BuildCappedTimeSeries(
        IReadOnlyList<ReportMetricPointModel> points,
        Func<ReportMetricPointModel, double> valueSelector,
        double maximum)
    {
        double?[] values = points
            .Select(point => valueSelector(point))
            .Select(value => double.IsFinite(value) ? (double?)value : null)
            .ToArray();
        int boundaryIndex = -1;
        for (int index = 0; index < points.Count; index++)
        {
            if (!points[index].IsInterpolated && values[index] is double value && value > maximum)
            {
                boundaryIndex = index;
                break;
            }
        }

        if (boundaryIndex < 0)
        {
            return new CappedMetricSeries(values, null);
        }

        int previousSimulationIndex = -1;
        for (int index = boundaryIndex - 1; index >= 0; index--)
        {
            if (!points[index].IsInterpolated)
            {
                previousSimulationIndex = index;
                break;
            }
        }

        if (previousSimulationIndex >= 0 &&
            values[previousSimulationIndex] is double previousValue &&
            boundaryIndex - previousSimulationIndex == 2 &&
            points[boundaryIndex - 1].IsInterpolated)
        {
            values[boundaryIndex - 1] = (previousValue + maximum) / 2d;
        }

        for (int index = boundaryIndex; index < values.Length; index++)
        {
            values[index] = null;
        }

        return new CappedMetricSeries(values, boundaryIndex);
    }

    internal static string FormatMetricHc5Caption(double hc5) => $"при HC5 {FormatHc5(hc5)}%";

    private void DrawAssessmentResultTable(XGraphics graphics, double x, double y, double width)
    {
        string[] headers = ["Тип потока", "HC5", "WT", "TTD", "IS", "LW", "Оценка"];
        string[] values =
        [
            model.Assessment.TrafficProfile,
            $"{Format(model.Assessment.Result.Hc5)}%",
            $"{Format(model.Assessment.Result.Wt)} с",
            $"{Format(model.Assessment.Result.Ttd)} с",
            Format(model.Assessment.Result.IntermediateStops),
            $"{Format(model.Assessment.Result.LongWaitPercent)}%",
            string.Empty,
        ];
        double cellWidth = width / headers.Length;
        for (int index = 0; index < headers.Length; index++)
        {
            double cellX = x + index * cellWidth;
            DrawTableCell(graphics, cellX, y, cellWidth, 27.5, headers[index], Regular(8.2), Deep60, Panel, XParagraphAlignment.Left, 8);
            DrawTableCell(graphics, cellX, y + 27.5, cellWidth, 27.5, values[index], SemiBold(8.2), Deep, XColors.White, XParagraphAlignment.Left, 8);
            if (index == headers.Length - 1)
            {
                DrawStars(graphics, cellX + 8, y + 36, model.Assessment.Rating, 5.4, Deep, Deep60);
            }
        }
    }

    private void BuildLiftPagePlans()
    {
        const int elevatorsPerPage = 7;
        const int firstPageFloorRows = 14;
        const int continuationFloorRows = 29;
        IReadOnlyList<ReportLiftModel> lifts = model.LiftGroup.Lifts;
        IReadOnlyList<ReportFloorServiceModel> serviceRows = GroupServiceRows(model.LiftGroup.ServiceMatrix);

        for (int elevatorStart = 0; elevatorStart < Math.Max(lifts.Count, 1); elevatorStart += elevatorsPerPage)
        {
            int elevatorCount = Math.Min(elevatorsPerPage, Math.Max(lifts.Count - elevatorStart, 0));
            int floorStart = 0;
            int firstCount = Math.Min(firstPageFloorRows, serviceRows.Count);
            int capturedElevatorStart = elevatorStart;
            int capturedElevatorCount = elevatorCount;
            int capturedFirstCount = firstCount;
            pages.Add(new PagePlan(false, graphics => DrawLiftPage(
                graphics,
                capturedElevatorStart,
                capturedElevatorCount,
                0,
                capturedFirstCount,
                firstPage: true,
                lastPageForChunk: capturedFirstCount >= serviceRows.Count,
                serviceRows: serviceRows)));
            floorStart += firstCount;

            while (floorStart < serviceRows.Count)
            {
                int count = Math.Min(continuationFloorRows, serviceRows.Count - floorStart);
                int capturedFloorStart = floorStart;
                int capturedCount = count;
                bool last = floorStart + count >= serviceRows.Count;
                pages.Add(new PagePlan(false, graphics => DrawLiftPage(
                    graphics,
                    capturedElevatorStart,
                    capturedElevatorCount,
                    capturedFloorStart,
                    capturedCount,
                    firstPage: false,
                    lastPageForChunk: last,
                    serviceRows: serviceRows)));
                floorStart += count;
            }
        }
    }

    private void DrawLiftPage(
        XGraphics graphics,
        int elevatorStart,
        int elevatorCount,
        int floorStart,
        int floorCount,
        bool firstPage,
        bool lastPageForChunk,
        IReadOnlyList<ReportFloorServiceModel> serviceRows)
    {
        string suffix = firstPage ? string.Empty : " · продолжение";
        DrawPageTitle(graphics, $"Лифтовая группа{suffix}", "Конфигурация оборудования и зона обслуживания");
        IReadOnlyList<ReportLiftModel> lifts = model.LiftGroup.Lifts.Skip(elevatorStart).Take(elevatorCount).ToArray();

        double tableY;
        if (firstPage)
        {
            int configurationCount = ReportLiftConfiguration.CountDistinct(model.LiftGroup.Lifts);
            DrawStatStrip(
                graphics,
                154,
                [
                    (model.LiftGroup.Lifts.Count.ToString(CultureInfo.InvariantCulture), "лифтов в группе"),
                    (configurationCount.ToString(CultureInfo.InvariantCulture), "конфигураций оборудования"),
                    (SummarizeLiftValues(model.LiftGroup.Lifts, lift => lift.CapacityKg, "кг"), "грузоподъёмность в группе"),
                    (SummarizeLiftValues(model.LiftGroup.Lifts, lift => lift.SpeedMetresPerSecond, "м/с"), "скорость в группе"),
                ],
                61);
            tableY = 231;
            tableY = DrawEquipmentTable(graphics, tableY, lifts);
            DrawString(graphics, $"Матрица обслуживаемых этажей · {model.LiftGroup.ServedFloorSummary}", SemiBold(10), Deep,
                new XRect(PageLeft, tableY + 17, ContentWidth, 14), XStringFormats.TopLeft);
            tableY += 39;
        }
        else
        {
            DrawString(graphics, $"Матрица обслуживаемых этажей · {model.LiftGroup.ServedFloorSummary}", SemiBold(10), Deep,
                new XRect(PageLeft, 137, ContentWidth, 14), XStringFormats.TopLeft);
            tableY = 159;
        }

        DrawServiceMatrix(graphics, tableY, lifts, elevatorStart, floorStart, floorCount, serviceRows);
        double noteY = tableY + 16 + floorCount * 17 + 15;
        if (noteY + 42 <= ContentBottom)
        {
            DrawInfoNote(graphics, noteY,
                "● этаж обслуживается · ЦО — центральное открывание · ТО — телескопическое открывание.");
        }
        _ = lastPageForChunk;
    }

    private double DrawEquipmentTable(XGraphics graphics, double y, IReadOnlyList<ReportLiftModel> lifts)
    {
        int columns = Math.Max(lifts.Count, 1);
        double labelWidth = 154;
        double valueWidth = (ContentWidth - labelWidth) / columns;
        const double headerHeight = 16;
        const double rowHeight = 17;

        DrawTableCell(graphics, PageLeft, y, labelWidth, headerHeight, "Параметр", SemiBold(7.3), XColors.White, Blue, XParagraphAlignment.Left, 7);
        for (int index = 0; index < columns; index++)
        {
            string label = index < lifts.Count ? $"Лифт {lifts[index].Number:00}" : "—";
            DrawTableCell(graphics, PageLeft + labelWidth + index * valueWidth, y, valueWidth, headerHeight,
                label, SemiBold(7.3), XColors.White, Blue, XParagraphAlignment.Center, 4);
        }

        (string Label, Func<ReportLiftModel, string> Value)[] rows =
        [
            ("Грузоподъёмность, кг", lift => lift.CapacityKg),
            ("Площадь кабины, м²", lift => Format(lift.CabinAreaSquareMetres, 2)),
            ("Скорость, м/с", lift => lift.SpeedMetresPerSecond),
            ("Ускорение, м/с²", lift => lift.AccelerationMetresPerSecondSquared),
            ("Рывок, м/с³", lift => lift.JerkMetresPerSecondCubed),
            ("Задержка пуска, с", lift => lift.MotorStartDelaySeconds),
            ("Ширина дверей, мм", lift => lift.DoorWidthMillimetres),
            ("Тип дверей", lift => lift.DoorType),
            ("Преоткрывание, с", lift => lift.DoorPreOpeningSeconds),
            ("Открытие дверей, с", lift => lift.DoorOpeningSeconds),
            ("Закрытие дверей, с", lift => lift.DoorClosingSeconds),
            ("Задержка световой завесы, с", lift => lift.LightCurtainDelaySeconds),
        ];

        for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            double rowY = y + headerHeight + rowIndex * rowHeight;
            DrawTableCell(graphics, PageLeft, rowY, labelWidth, rowHeight, rows[rowIndex].Label,
                SemiBold(7.3), Deep, XColors.White, XParagraphAlignment.Left, 7);
            for (int column = 0; column < columns; column++)
            {
                string value = column < lifts.Count ? rows[rowIndex].Value(lifts[column]) : "—";
                DrawTableCell(graphics, PageLeft + labelWidth + column * valueWidth, rowY, valueWidth, rowHeight,
                    DisplayValue(value), Regular(7.3), Deep, XColors.White, XParagraphAlignment.Center, 4);
            }
        }

        double controlY = y + headerHeight + rows.Length * rowHeight;
        DrawTableCell(graphics, PageLeft, controlY, labelWidth, rowHeight, "Система управления",
            SemiBold(7.3), Deep, XColors.White, XParagraphAlignment.Left, 7);
        DrawTableCell(graphics, PageLeft + labelWidth, controlY, ContentWidth - labelWidth, rowHeight,
            DisplayValue(model.LiftGroup.ControlSystem), Regular(7.3), Deep, XColors.White, XParagraphAlignment.Left, 7);
        return controlY + rowHeight;
    }

    private void DrawServiceMatrix(
        XGraphics graphics,
        double y,
        IReadOnlyList<ReportLiftModel> lifts,
        int elevatorStart,
        int floorStart,
        int floorCount,
        IReadOnlyList<ReportFloorServiceModel> serviceRows)
    {
        int columns = Math.Max(lifts.Count, 1);
        double labelWidth = 154;
        double valueWidth = (ContentWidth - labelWidth) / columns;
        const double headerHeight = 16;
        const double rowHeight = 17;
        DrawTableCell(graphics, PageLeft, y, labelWidth, headerHeight, "Этаж", SemiBold(7.3), XColors.White, Blue, XParagraphAlignment.Left, 7);
        for (int index = 0; index < columns; index++)
        {
            string label = index < lifts.Count ? $"Лифт {lifts[index].Number:00}" : "—";
            DrawTableCell(graphics, PageLeft + labelWidth + index * valueWidth, y, valueWidth, headerHeight,
                label, SemiBold(7.3), XColors.White, Blue, XParagraphAlignment.Center, 4);
        }

        for (int rowIndex = 0; rowIndex < floorCount; rowIndex++)
        {
            ReportFloorServiceModel floor = serviceRows[floorStart + rowIndex];
            double rowY = y + headerHeight + rowIndex * rowHeight;
            DrawTableCell(graphics, PageLeft, rowY, labelWidth, rowHeight, floor.Floor,
                SemiBold(7.3), Deep, XColors.White, XParagraphAlignment.Left, 7);
            for (int column = 0; column < columns; column++)
            {
                int serviceIndex = elevatorStart + column;
                bool served = serviceIndex < floor.ServedByLift.Count && floor.ServedByLift[serviceIndex];
                string mark = served ? string.Empty : "—";
                DrawTableCell(graphics, PageLeft + labelWidth + column * valueWidth, rowY, valueWidth, rowHeight,
                    mark, Regular(7.3), served ? Blue : Deep60,
                    XColors.White, XParagraphAlignment.Center, 4);
                if (served)
                {
                    double dotX = PageLeft + labelWidth + column * valueWidth + valueWidth / 2;
                    graphics.DrawEllipse(new XSolidBrush(Blue), dotX - 2, rowY + rowHeight / 2 - 2, 4, 4);
                }
            }
        }
    }

    private static IReadOnlyList<ReportFloorServiceModel> GroupServiceRows(
        IReadOnlyList<ReportFloorServiceModel> rows)
    {
        if (rows.Count < 2) return rows;

        List<ReportFloorServiceModel> grouped = [];
        int start = 0;
        while (start < rows.Count)
        {
            int end = start;
            while (end + 1 < rows.Count && rows[start].ServedByLift.SequenceEqual(rows[end + 1].ServedByLift))
            {
                end++;
            }

            grouped.Add(new ReportFloorServiceModel(
                BuildRangeLabel(rows[start].Floor, rows[end].Floor, end - start + 1),
                rows[start].ServedByLift));
            start = end + 1;
        }

        return grouped;
    }

    private void BuildBuildingPagePlans()
    {
        const int firstPageRows = 26;
        const int continuationRows = 31;
        int start = 0;
        int firstCount = Math.Min(firstPageRows, model.Building.Floors.Count);
        pages.Add(new PagePlan(false, graphics => DrawBuildingPage(graphics, 0, firstCount, firstPage: true,
            isLastPage: firstCount >= model.Building.Floors.Count)));
        start += firstCount;

        while (start < model.Building.Floors.Count)
        {
            int count = Math.Min(continuationRows, model.Building.Floors.Count - start);
            int capturedStart = start;
            int capturedCount = count;
            bool last = start + count >= model.Building.Floors.Count;
            pages.Add(new PagePlan(false, graphics => DrawBuildingPage(graphics, capturedStart, capturedCount, firstPage: false, isLastPage: last)));
            start += count;
        }
    }

    private void DrawBuildingPage(XGraphics graphics, int rowStart, int rowCount, bool firstPage, bool isLastPage)
    {
        DrawPageTitle(graphics, firstPage ? "Здание" : "Здание · продолжение", "Этажность, назначение и расчётное население");
        double tableY;
        if (firstPage)
        {
            DrawStatStrip(graphics, 154,
                [
                    (model.Building.TotalLevels.ToString(CultureInfo.InvariantCulture), "уровней"),
                    (Format(model.Building.CalculatedPopulation), "расчётное население"),
                    (model.Building.PresenceSummary, "коэффициент присутствия"),
                    (model.Building.ServedFloorSummary, "остановок обслуживается / всего"),
                ],
                67);
            tableY = 237;
        }
        else
        {
            tableY = 137;
        }

        DrawBuildingTable(graphics, tableY, rowStart, rowCount, isLastPage);
    }

    private void DrawBuildingTable(XGraphics graphics, double y, int rowStart, int rowCount, bool includeTotal)
    {
        double columnWidth = ContentWidth / 7;
        double[] widths = Enumerable.Repeat(columnWidth, 7).ToArray();
        string[] headers = ["Этаж", "Высота, м", "Отметка, м", "Назначение", "Насел.", "Коэф.", "Итог"];
        const double headerHeight = 26;
        DrawHeaderRow(graphics, PageLeft, y, widths, headers, headerHeight, 7.3, centerAll: true);
        const double rowHeight = 17;

        for (int index = 0; index < rowCount; index++)
        {
            ReportBuildingFloorModel floor = model.Building.Floors[rowStart + index];
            string[] values =
            [
                floor.Floor,
                Format(floor.HeightMetres),
                Format(floor.ElevationMetres),
                floor.Function,
                Format(floor.Population),
                Format(floor.PresenceFactor),
                Format(floor.CalculatedPopulation),
            ];
            DrawDataRow(graphics, PageLeft, y + headerHeight + index * rowHeight, widths, values, rowHeight, 7.3,
                firstColumnBold: true, lastColumnBold: true, centerAll: true);
        }

        if (includeTotal)
        {
            double totalY = y + headerHeight + rowCount * rowHeight;
            double labelWidth = widths.Take(6).Sum();
            DrawTableCell(graphics, PageLeft, totalY, labelWidth, rowHeight, "Итоговое расчётное население",
                SemiBold(7.3), Deep, XColors.White, XParagraphAlignment.Left, 7);
            DrawTableCell(graphics, PageLeft + labelWidth, totalY, widths[6], rowHeight, Format(model.Building.CalculatedPopulation),
                SemiBold(7.3), Deep, XColors.White, XParagraphAlignment.Center, 4);
        }
    }

    private void BuildTrafficPagePlans()
    {
        const int firstPageRows = 14;
        const int continuationRows = 29;
        IReadOnlyList<ReportTrafficFloorModel> trafficRows = GroupTrafficRows(model.Traffic.Floors);
        int start = 0;
        int firstCount = Math.Min(firstPageRows, trafficRows.Count);
        pages.Add(new PagePlan(false, graphics => DrawTrafficPage(graphics, 0, firstCount, firstPage: true,
            lastPage: firstCount >= trafficRows.Count, trafficRows: trafficRows)));
        start += firstCount;

        while (start < trafficRows.Count)
        {
            int count = Math.Min(continuationRows, trafficRows.Count - start);
            int capturedStart = start;
            int capturedCount = count;
            bool last = start + count >= trafficRows.Count;
            pages.Add(new PagePlan(false, graphics => DrawTrafficPage(graphics, capturedStart, capturedCount, firstPage: false,
                lastPage: last, trafficRows: trafficRows)));
            start += count;
        }
    }

    private void DrawTrafficPage(
        XGraphics graphics,
        int rowStart,
        int rowCount,
        bool firstPage,
        bool lastPage,
        IReadOnlyList<ReportTrafficFloorModel> trafficRows)
    {
        DrawPageTitle(graphics, firstPage ? "Пассажиропоток" : "Пассажиропоток · продолжение",
            "Распределение направления и населения по этажам");
        double tableY;
        if (firstPage)
        {
            DrawFlowCards(graphics, 154);
            DrawStatStrip(graphics, 233,
                [
                    (Format(model.Building.CalculatedPopulation), "расчётное население"),
                    (model.Building.OccupiedLevels.ToString(CultureInfo.InvariantCulture), "заселённых этажей"),
                    (model.Traffic.SimulationCount.ToString(CultureInfo.InvariantCulture), "расчётов моделированием"),
                    (model.Traffic.DisplayPointCount.ToString(CultureInfo.InvariantCulture), "точек с интерполяцией"),
                ],
                51);
            DrawString(graphics, "Распределение по этажам", SemiBold(10), Deep,
                new XRect(PageLeft, 301, ContentWidth, 14), XStringFormats.TopLeft);
            tableY = 323;
        }
        else
        {
            DrawString(graphics, "Распределение по этажам", SemiBold(10), Deep,
                new XRect(PageLeft, 137, ContentWidth, 14), XStringFormats.TopLeft);
            tableY = 159;
        }

        double tableBottom = DrawTrafficTable(graphics, tableY, rowStart, rowCount, trafficRows);
        if (firstPage && tableBottom + 57 <= ContentBottom)
        {
            DrawInfoNote(graphics, tableBottom + 15,
                "Диапазон объединяется только при совпадении населения, коэффициента присутствия и всех направлений потока. Проценты в строке диапазона указаны суммарно.");
        }
        _ = lastPage;
    }

    private void DrawFlowCards(XGraphics graphics, double y)
    {
        (string Name, double Value, XColor Color)[] cards =
        [
            ("Входящий", model.Traffic.IncomingPercent, Blue),
            ("Выходящий", model.Traffic.OutgoingPercent, Gold),
            ("Межэтажный", model.Traffic.InterfloorPercent, Blue40),
        ];
        double cardWidth = (ContentWidth - 20) / 3;
        for (int index = 0; index < cards.Length; index++)
        {
            double x = PageLeft + index * (cardWidth + 10);
            graphics.DrawRectangle(new XPen(Line, 1), XBrushes.White, x, y, cardWidth, 61);
            DrawString(graphics, cards[index].Name, Regular(7), Deep60, new XRect(x + 13, y + 23, cardWidth - 70, 10), XStringFormats.TopLeft);
            DrawString(graphics, $"{Format(cards[index].Value)}%", SemiBold(18), Deep,
                new XRect(x + cardWidth - 66, y + 12, 53, 23), XStringFormats.TopRight);
            graphics.DrawRectangle(new XSolidBrush(Deep10), x + 13, y + 43, cardWidth - 26, 5);
            graphics.DrawRectangle(new XSolidBrush(cards[index].Color), x + 13, y + 43,
                (cardWidth - 26) * Math.Clamp(cards[index].Value / 100d, 0, 1), 5);
        }
    }

    private double DrawTrafficTable(
        XGraphics graphics,
        double y,
        int rowStart,
        int rowCount,
        IReadOnlyList<ReportTrafficFloorModel> trafficRows)
    {
        double[] widths = [97, 49, 78, 62, 82, 82, 81];
        string[] headers = ["Этаж / диапазон", "Этажей", "Население", "Коэф.", "Входящий", "Выходящий", "Межэтажный"];
        const double headerHeight = 34.406;
        const double rowHeight = 24.203;
        DrawHeaderRow(graphics, PageLeft, y, widths, headers, headerHeight, 7.3, centerAll: true);
        for (int index = 0; index < rowCount; index++)
        {
            ReportTrafficFloorModel floor = trafficRows[rowStart + index];
            string[] values = [floor.Floor, floor.FloorCount.ToString(CultureInfo.InvariantCulture), floor.Population,
                floor.PresenceFactor, floor.Incoming, floor.Outgoing, floor.Interfloor];
            DrawDataRow(graphics, PageLeft, y + headerHeight + index * rowHeight, widths, values, rowHeight, 7.3,
                firstColumnBold: true, centerAll: true);
        }

        return y + headerHeight + rowCount * rowHeight;
    }

    internal static IReadOnlyList<ReportTrafficFloorModel> GroupTrafficRows(
        IReadOnlyList<ReportTrafficFloorModel> rows)
    {
        if (rows.Count < 2) return rows;

        List<ReportTrafficFloorModel> grouped = [];
        int start = 0;
        while (start < rows.Count)
        {
            int end = start;
            while (end + 1 < rows.Count && HaveIdenticalTrafficData(rows[start], rows[end + 1]))
            {
                end++;
            }

            ReportTrafficFloorModel first = rows[start];
            int floorCount = rows.Skip(start).Take(end - start + 1).Sum(row => row.FloorCount);
            grouped.Add(first with
            {
                Floor = BuildRangeLabel(first.Floor, rows[end].Floor, end - start + 1),
                FloorCount = floorCount,
                Incoming = FormatGroupedTrafficShare(rows, start, end, row => row.IncomingPercentValue, first.Incoming),
                Outgoing = FormatGroupedTrafficShare(rows, start, end, row => row.OutgoingPercentValue, first.Outgoing),
                Interfloor = FormatGroupedTrafficShare(rows, start, end, row => row.InterfloorPercentValue, first.Interfloor),
            });
            start = end + 1;
        }

        return grouped;
    }

    private static bool HaveIdenticalTrafficData(ReportTrafficFloorModel left, ReportTrafficFloorModel right) =>
        left.Population == right.Population &&
        left.PresenceFactor == right.PresenceFactor &&
        left.Incoming == right.Incoming &&
        left.Outgoing == right.Outgoing &&
        left.Interfloor == right.Interfloor &&
        left.IncomingPercentValue == right.IncomingPercentValue &&
        left.OutgoingPercentValue == right.OutgoingPercentValue &&
        left.InterfloorPercentValue == right.InterfloorPercentValue;

    private static string FormatGroupedTrafficShare(
        IReadOnlyList<ReportTrafficFloorModel> rows,
        int start,
        int end,
        Func<ReportTrafficFloorModel, double?> valueSelector,
        string fallback)
    {
        if (end <= start || fallback == "—")
        {
            return fallback;
        }

        double total = 0d;
        for (int index = start; index <= end; index++)
        {
            if (valueSelector(rows[index]) is not double value || !double.IsFinite(value))
            {
                return fallback;
            }

            total += value;
        }

        int suffixIndex = fallback.IndexOf('%');
        string suffix = suffixIndex >= 0 ? fallback[(suffixIndex + 1)..] : string.Empty;
        return $"{total.ToString("0.0", CultureInfo.GetCultureInfo("ru-RU"))}%{suffix}";
    }

    private static string BuildRangeLabel(string first, string last, int count) =>
        count <= 1 || first == last ? first : $"{first}–{last}";

    private void DrawCriteria(XGraphics graphics)
    {
        DrawPageTitle(graphics, "Критерии оценки", $"Активный профиль: {model.Criteria.ActiveProfile}");
        DrawCriteriaTable(graphics, 154);
        DrawText(graphics,
            "Итоговая категория определяется по сочетанию HC5, WT и TTD. HC5 — не менее указанного значения; WT и TTD — менее указанного значения.",
            Regular(6.1), Deep60, new XRect(PageLeft, 380.8, ContentWidth, 9), XParagraphAlignment.Left, 9, maxLines: 1);
        DrawGlossary(graphics, 405.8);
        DrawLegalNote(graphics, 561.8);
    }

    private void DrawCriteriaTable(XGraphics graphics, double y)
    {
        double[] widths = [126, 135, 135, 135];
        string[] headers = ["Профиль потока", string.Empty, string.Empty, string.Empty];
        const double headerHeight = 25.2;
        DrawHeaderRow(graphics, PageLeft, y, widths, headers, headerHeight, 7.4);
        for (int column = 1; column < widths.Length; column++)
        {
            double starX = PageLeft + widths.Take(column).Sum() + (widths[column] - 37) / 2;
            DrawStars(graphics, starX, y + 9, column + 2, 3.4, XColors.White, Blue20);
        }
        const double rowHeight = 48.4;
        for (int index = 0; index < model.Criteria.Profiles.Count; index++)
        {
            ReportCriteriaProfileModel profile = model.Criteria.Profiles[index];
            ReportCriteriaThresholdModel[] thresholds = [profile.ThreeStars, profile.FourStars, profile.FiveStars];
            double rowY = y + headerHeight + index * rowHeight;
            bool active = profile.Name.Equals(model.Criteria.ActiveProfile, StringComparison.Ordinal);
            graphics.DrawRectangle(new XPen(Line, 0.8), XBrushes.White, PageLeft, rowY, widths[0], rowHeight);
            DrawText(graphics, profile.Name, active ? SemiBold(7.4) : Regular(7.4), Deep,
                new XRect(PageLeft + 7, rowY + 7.5, widths[0] - 14, 11), XParagraphAlignment.Left, 10.2, maxLines: 1);
            for (int column = 0; column < thresholds.Length; column++)
            {
                ReportCriteriaThresholdModel threshold = thresholds[column];
                bool selected = active && model.Assessment.Rating == column + 3;
                string value = $"HC5 ≥ {threshold.Hc5}%\nWT < {threshold.WtSeconds} с\nTTD < {threshold.TtdSeconds} с";
                double cellX = PageLeft + widths[0] + widths.Skip(1).Take(column).Sum();
                graphics.DrawRectangle(new XPen(Line, 0.8), new XSolidBrush(selected ? Gold10 : XColors.White),
                    cellX, rowY, widths[column + 1], rowHeight);
                DrawText(graphics, value, selected ? SemiBold(7.1) : Regular(7.1), Deep,
                    new XRect(cellX + 7, rowY + 7.5, widths[column + 1] - 14, 36),
                    XParagraphAlignment.Center, 11.8, maxLines: 3);
                if (selected)
                {
                    graphics.DrawRectangle(new XSolidBrush(Gold), cellX, rowY + rowHeight - 2, widths[column + 1], 2);
                }
            }
        }
    }

    private void DrawGlossary(XGraphics graphics, double y)
    {
        (string Title, string Text)[] items =
        [
            ("HC5 · провозная способность", "Доля общего населения, перевозимая за пять минут в часы пик, обычно при коэффициенте загрузки 80%."),
            ("WT · среднее ожидание", "От регистрации вызова до начала открытия дверей прибывшего лифта."),
            ("TTD · время до назначения", "От регистрации вызова до начала открытия дверей на этаже назначения."),
            ("IS · промежуточные остановки", "Среднее количество остановок за круговой рейс: от основного посадочного этажа до возвращения."),
            ("LW · долгое ожидание", "Доля пассажиров, ожидавших лифт более 90 секунд."),
            ($"Тип потока · {model.Assessment.TrafficProfile}", "Входящий / выходящий / межэтажный пассажиропоток для выбранного часа пик."),
        ];
        double colWidth = (ContentWidth - 16) / 2;
        for (int index = 0; index < items.Length; index++)
        {
            int row = index / 2;
            int column = index % 2;
            double itemX = PageLeft + column * (colWidth + 16);
            double itemY = y + row * 51;
            graphics.DrawLine(new XPen(Line, 1), itemX, itemY, itemX + colWidth, itemY);
            DrawString(graphics, items[index].Title, SemiBold(7.5), Deep,
                new XRect(itemX, itemY + 8, colWidth, 11), XStringFormats.TopLeft);
            DrawText(graphics, items[index].Text, Regular(6.6), Deep60,
                new XRect(itemX, itemY + 23, colWidth, 20), XParagraphAlignment.Left, 10, maxLines: 2);
        }
    }

    private void DrawLegalNote(XGraphics graphics, double y)
    {
        const double height = 84.7;
        const double contentX = PageLeft + 14;
        const double contentWidth = ContentWidth - 25;
        graphics.DrawRectangle(new XSolidBrush(Panel), PageLeft, y, ContentWidth, height);
        graphics.DrawRectangle(new XSolidBrush(Deep20), PageLeft, y, 3, height);
        DrawString(graphics, "Примечание", SemiBold(5.7), Deep,
            new XRect(contentX, y + 9, contentWidth, 9), XStringFormats.TopLeft);

        string legalNote = model.Criteria.LegalNote.Replace("\r", string.Empty, StringComparison.Ordinal);
        int separator = legalNote.IndexOf("\n\n", StringComparison.Ordinal);
        string firstParagraph = separator >= 0 ? legalNote[..separator] : legalNote;
        string secondParagraph = separator >= 0 ? legalNote[(separator + 2)..] : string.Empty;
        DrawText(graphics, firstParagraph, Regular(5.7), Deep60,
            new XRect(contentX, y + 22.1, contentWidth, 24.3), XParagraphAlignment.Left, 8.1, maxLines: 3);
        if (!string.IsNullOrWhiteSpace(secondParagraph))
        {
            DrawText(graphics, secondParagraph, Regular(5.7), Deep60,
                new XRect(contentX, y + 51.4, contentWidth, 24.3), XParagraphAlignment.Left, 8.1, maxLines: 3);
        }
    }

    private void DrawStatStrip(
        XGraphics graphics,
        double y,
        IReadOnlyList<(string Value, string Label)> stats,
        double height)
    {
        double cellWidth = ContentWidth / Math.Max(stats.Count, 1);
        for (int index = 0; index < stats.Count; index++)
        {
            double x = PageLeft + index * cellWidth;
            graphics.DrawRectangle(new XPen(Line, 1), XBrushes.White, x, y, cellWidth, height);
            DrawText(graphics, stats[index].Value, SemiBold(13), Deep,
                new XRect(x + 11, y + 9, cellWidth - 22, 18), XParagraphAlignment.Left, 16, maxLines: 1);
            DrawText(graphics, stats[index].Label, Regular(7), Deep60,
                new XRect(x + 11, y + 30, cellWidth - 22, 16), XParagraphAlignment.Left, 10, maxLines: 2);
        }
    }

    private static void DrawInfoNote(XGraphics graphics, double y, string text)
    {
        const double height = 42;
        graphics.DrawRectangle(new XSolidBrush(Panel), PageLeft, y, ContentWidth, height);
        graphics.DrawRectangle(new XSolidBrush(Blue20), PageLeft, y, 3, height);
        double textX = PageLeft + 15;
        if (text.StartsWith("● ", StringComparison.Ordinal))
        {
            graphics.DrawEllipse(new XSolidBrush(Blue), textX, y + 14, 3.5, 3.5);
            text = text[2..];
            textX += 8;
        }

        DrawText(graphics, text, Regular(7), Deep60, new XRect(textX, y + 11, PageWidth - PageRight - textX - 15, 20),
            XParagraphAlignment.Left, 11, maxLines: 2);
    }

    private void DrawLogo(XGraphics graphics, double x, double y, double width, double height)
    {
        XRect destination = FitLogoDestination(x, y, width, height);
        graphics.DrawImage(logo, destination, VisibleLogoSource, XGraphicsUnit.Point);
    }

    internal static XRect FitLogoDestination(double x, double y, double width, double height)
    {
        double scale = Math.Min(width / VisibleLogoSource.Width, height / VisibleLogoSource.Height);
        double fittedWidth = VisibleLogoSource.Width * scale;
        double fittedHeight = VisibleLogoSource.Height * scale;
        return new XRect(
            x + (width - fittedWidth) / 2,
            y + (height - fittedHeight) / 2,
            fittedWidth,
            fittedHeight);
    }

    internal static string SummarizeLiftValues(
        IReadOnlyList<ReportLiftModel> lifts,
        Func<ReportLiftModel, string> selector,
        string unit)
    {
        string[] values = lifts
            .Select(selector)
            .Where(value => !string.IsNullOrWhiteSpace(value) && value != "—" && value != "-")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return values.Length switch
        {
            0 => "—",
            <= 3 => $"{string.Join(" / ", values)} {unit}",
            _ => $"{values.Length} значений",
        };
    }

    private static void DrawHeaderRow(
        XGraphics graphics,
        double x,
        double y,
        IReadOnlyList<double> widths,
        IReadOnlyList<string> values,
        double height,
        double fontSize,
        bool centerAll = false)
    {
        double cellX = x;
        for (int index = 0; index < widths.Count; index++)
        {
            DrawTableCell(graphics, cellX, y, widths[index], height, values[index], SemiBold(fontSize), XColors.White, Blue,
                centerAll || index != 0 ? XParagraphAlignment.Center : XParagraphAlignment.Left,
                centerAll || index != 0 ? 4 : 7);
            cellX += widths[index];
        }
    }

    private static void DrawDataRow(
        XGraphics graphics,
        double x,
        double y,
        IReadOnlyList<double> widths,
        IReadOnlyList<string> values,
        double height,
        double fontSize,
        bool firstColumnBold = false,
        bool lastColumnBold = false,
        bool centerAll = false)
    {
        double cellX = x;
        for (int index = 0; index < widths.Count; index++)
        {
            bool bold = firstColumnBold && index == 0 || lastColumnBold && index == widths.Count - 1;
            DrawTableCell(graphics, cellX, y, widths[index], height, DisplayValue(values[index]),
                bold ? SemiBold(fontSize) : Regular(fontSize), Deep, XColors.White,
                centerAll || index != 0 && index != 3 ? XParagraphAlignment.Center : XParagraphAlignment.Left,
                centerAll || index != 0 && index != 3 ? 4 : 7, lineHeight: fontSize + 3);
            cellX += widths[index];
        }
    }

    private static void DrawTableCell(
        XGraphics graphics,
        double x,
        double y,
        double width,
        double height,
        string text,
        XFont font,
        XColor textColor,
        XColor fillColor,
        XParagraphAlignment alignment,
        double horizontalPadding = 4,
        double? lineHeight = null)
    {
        graphics.DrawRectangle(new XPen(Line, 0.8), new XSolidBrush(fillColor), x, y, width, height);
        double actualLineHeight = lineHeight ?? font.Size + 3;
        DrawText(graphics, text, font, textColor,
            new XRect(x + horizontalPadding, y + Math.Max(2, (height - actualLineHeight) / 2), width - horizontalPadding * 2, height - 4),
            alignment, actualLineHeight, maxLines: Math.Max(1, (int)Math.Floor((height - 4) / actualLineHeight)));
    }

    private static void DrawString(
        XGraphics graphics,
        string text,
        XFont font,
        XColor color,
        XRect rect,
        XStringFormat format)
    {
        graphics.DrawString(DisplayValue(text), font, new XSolidBrush(color), rect, format);
    }

    private static void DrawText(
        XGraphics graphics,
        string? text,
        XFont font,
        XColor color,
        XRect rect,
        XParagraphAlignment alignment,
        double lineHeight,
        int? maxLines = null)
    {
        string value = DisplayValue(text);
        IReadOnlyList<string> lines = WrapText(graphics, value, font, rect.Width, maxLines);
        int lineCount = maxLines.HasValue ? Math.Min(maxLines.Value, lines.Count) : lines.Count;
        for (int index = 0; index < lineCount; index++)
        {
            string line = lines[index];
            if (maxLines.HasValue && index == lineCount - 1 && lines.Count > lineCount)
            {
                line = Ellipsize(graphics, line, font, rect.Width);
            }

            XStringFormat format = alignment switch
            {
                XParagraphAlignment.Center => XStringFormats.TopCenter,
                XParagraphAlignment.Right => XStringFormats.TopRight,
                _ => XStringFormats.TopLeft,
            };
            graphics.DrawString(line, font, new XSolidBrush(color),
                new XRect(rect.X, rect.Y + index * lineHeight, rect.Width, lineHeight), format);
        }
    }

    private static IReadOnlyList<string> WrapText(XGraphics graphics, string text, XFont font, double maxWidth, int? maxLines)
    {
        List<string> lines = [];
        foreach (string paragraph in text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            string current = string.Empty;
            foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = current.Length == 0 ? word : $"{current} {word}";
                if (graphics.MeasureString(candidate, font).Width <= maxWidth || current.Length == 0)
                {
                    current = candidate;
                    continue;
                }

                lines.Add(current);
                current = word;
            }

            if (current.Length > 0)
            {
                lines.Add(current);
            }
        }

        return lines.Count == 0 ? [string.Empty] : lines;
    }

    private static string Ellipsize(XGraphics graphics, string text, XFont font, double maxWidth)
    {
        const string ellipsis = "…";
        string candidate = text;
        while (candidate.Length > 1 && graphics.MeasureString(candidate + ellipsis, font).Width > maxWidth)
        {
            candidate = candidate[..^1];
        }

        return candidate.TrimEnd() + ellipsis;
    }

    private static AxisScale BuildAxisScale(double maxValue)
    {
        maxValue = Math.Max(maxValue, 0.001);
        double roughStep = maxValue / 4;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(roughStep)));
        double residual = roughStep / magnitude;
        double niceResidual = residual switch
        {
            <= 1 => 1,
            <= 2 => 2,
            <= 3.5 => 2.5,
            <= 5 => 5,
            _ => 10,
        };
        double step = niceResidual * magnitude;
        double maximum = Math.Ceiling(maxValue / step) * step;
        int count = (int)Math.Round(maximum / step, MidpointRounding.AwayFromZero);
        return new AxisScale(maximum, Enumerable.Range(0, count + 1).Select(index => index * step).ToArray());
    }

    private static AxisScale BuildFixedAxisScale(double maximum, int intervalCount)
    {
        double step = maximum / intervalCount;
        return new AxisScale(maximum, Enumerable.Range(0, intervalCount + 1).Select(index => index * step).ToArray());
    }

    private static string AxisLabel(double value, string unit)
    {
        int precision = Math.Abs(value) < 10 && !NearlyInteger(value) ? 1 : 0;
        string formatted = Format(value, precision);
        return string.IsNullOrWhiteSpace(unit) ? formatted : $"{formatted} {unit}";
    }

    private static bool NearlyInteger(double value) => Math.Abs(value - Math.Round(value)) < 0.000001;

    private static string DisplayValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
    }

    private static string Format(double value, int maximumDecimals = 1)
    {
        if (!double.IsFinite(value))
        {
            return "—";
        }

        string format = maximumDecimals switch
        {
            <= 0 => "0",
            1 => "0.#",
            2 => "0.##",
            _ => "0.###",
        };
        return value.ToString(format, CultureInfo.GetCultureInfo("ru-RU"));
    }

    private static string FormatHc5(double value)
    {
        return value.ToString(NearlyInteger(value) ? "0" : "0.0", CultureInfo.GetCultureInfo("ru-RU"));
    }

    private static void DrawStars(
        XGraphics graphics,
        double x,
        double y,
        int rating,
        double outerRadius,
        XColor filledColor,
        XColor emptyColor)
    {
        rating = Math.Clamp(rating, 0, 5);
        double gap = outerRadius * 2.45;
        for (int index = 0; index < 5; index++)
        {
            double centerX = x + outerRadius + index * gap;
            double centerY = y + outerRadius;
            XPoint[] points = Enumerable.Range(0, 10)
                .Select(pointIndex =>
                {
                    double radius = pointIndex % 2 == 0 ? outerRadius : outerRadius * 0.46;
                    double angle = -Math.PI / 2 + pointIndex * Math.PI / 5;
                    return new XPoint(centerX + Math.Cos(angle) * radius, centerY + Math.Sin(angle) * radius);
                })
                .ToArray();
            if (index < rating)
            {
                graphics.DrawPolygon(new XSolidBrush(filledColor), points, XFillMode.Winding);
            }
            else
            {
                graphics.DrawPolygon(new XPen(emptyColor, 0.7), XBrushes.Transparent, points, XFillMode.Winding);
            }
        }
    }

    private static XFont Regular(double size) => new(ReportFontResolver.FamilyName, size, XFontStyleEx.Regular);

    private static XFont SemiBold(double size) => new(ReportFontResolver.SemiBoldFamilyName, size, XFontStyleEx.Regular);

    private static XFont ExtraBold(double size) => new(ReportFontResolver.ExtraBoldFamilyName, size, XFontStyleEx.Regular);

    public void Dispose()
    {
        logo.Dispose();
    }

    private sealed record PagePlan(bool IsCover, Action<XGraphics> Draw);

    private sealed record AxisScale(double Maximum, IReadOnlyList<double> Values);
}

internal sealed record CappedMetricSeries(IReadOnlyList<double?> Values, int? BoundaryIndex);
