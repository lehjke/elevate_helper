namespace ElevateHelperWinUI.Models.Reports;

internal sealed record ReportDocumentModel(
    ReportMetadataModel Metadata,
    ReportAssessmentModel Assessment,
    ReportLiftGroupModel LiftGroup,
    ReportBuildingModel Building,
    ReportTrafficModel Traffic,
    ReportCriteriaModel Criteria);

internal sealed record ReportMetadataModel(
    string ProjectName,
    string AddressOrCalculation,
    string Revision,
    string Author,
    DateTime GeneratedAt,
    string BuildingTypeLabel,
    string MethodLabel);

internal sealed record ReportAssessmentModel(
    int Rating,
    string TrafficProfile,
    string ElevatorSummary,
    int Hc5Maximum,
    double? TargetWt,
    IReadOnlyList<ReportMetricPointModel> SimulationPoints,
    IReadOnlyList<ReportMetricPointModel> DisplayPoints,
    ReportAssessmentResultModel Result,
    ReportCriteriaThresholdModel ActiveThreshold);

internal sealed record ReportMetricPointModel(
    double Hc5,
    double Wt,
    double Ttd,
    double IntermediateStops,
    double LongWaitPercent,
    bool IsInterpolated);

internal sealed record ReportAssessmentResultModel(
    double Hc5,
    double Wt,
    double Ttd,
    double IntermediateStops,
    double LongWaitPercent,
    int Rating);

internal sealed record ReportLiftGroupModel(
    string ControlSystem,
    string ServedFloorSummary,
    IReadOnlyList<ReportLiftModel> Lifts,
    IReadOnlyList<ReportFloorServiceModel> ServiceMatrix);

internal sealed record ReportLiftModel(
    int Number,
    string CapacityKg,
    double CabinAreaSquareMetres,
    string SpeedMetresPerSecond,
    string AccelerationMetresPerSecondSquared,
    string JerkMetresPerSecondCubed,
    string MotorStartDelaySeconds,
    string DoorWidthMillimetres,
    string DoorType,
    string DoorPreOpeningSeconds,
    string DoorOpeningSeconds,
    string DoorClosingSeconds,
    string LightCurtainDelaySeconds);

internal static class ReportLiftConfiguration
{
    internal static int CountDistinct(IReadOnlyList<ReportLiftModel> lifts) =>
        lifts.Select(BuildKey).Distinct(StringComparer.Ordinal).Count();

    internal static string DescribeLiftCount(int count) =>
        $"{count} {SelectRussianNoun(count, "лифт", "лифта", "лифтов")}";

    internal static string DescribeConfigurationCount(int count) =>
        $"{count} {SelectRussianNoun(count, "конфигурация", "конфигурации", "конфигураций")}";

    private static string BuildKey(ReportLiftModel lift) => string.Join('\u001f',
        lift.CapacityKg,
        lift.CabinAreaSquareMetres.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        lift.SpeedMetresPerSecond,
        lift.AccelerationMetresPerSecondSquared,
        lift.JerkMetresPerSecondCubed,
        lift.MotorStartDelaySeconds,
        lift.DoorWidthMillimetres,
        lift.DoorType,
        lift.DoorPreOpeningSeconds,
        lift.DoorOpeningSeconds,
        lift.DoorClosingSeconds,
        lift.LightCurtainDelaySeconds);

    private static string SelectRussianNoun(int count, string singular, string paucal, string plural)
    {
        int lastTwoDigits = Math.Abs(count) % 100;
        if (lastTwoDigits is >= 11 and <= 14)
        {
            return plural;
        }

        return (Math.Abs(count) % 10) switch
        {
            1 => singular,
            2 or 3 or 4 => paucal,
            _ => plural,
        };
    }
}

internal sealed record ReportFloorServiceModel(
    string Floor,
    IReadOnlyList<bool> ServedByLift);

internal sealed record ReportBuildingModel(
    int TotalLevels,
    int OccupiedLevels,
    double CalculatedPopulation,
    string PresenceSummary,
    string ServedFloorSummary,
    IReadOnlyList<ReportBuildingFloorModel> Floors);

internal sealed record ReportBuildingFloorModel(
    string Floor,
    double HeightMetres,
    double ElevationMetres,
    string Function,
    double Population,
    double PresenceFactor,
    double CalculatedPopulation);

internal sealed record ReportTrafficModel(
    double IncomingPercent,
    double OutgoingPercent,
    double InterfloorPercent,
    int SimulationCount,
    int DisplayPointCount,
    IReadOnlyList<ReportTrafficFloorModel> Floors);

internal sealed record ReportTrafficFloorModel(
    string Floor,
    int FloorCount,
    string Population,
    string PresenceFactor,
    string Incoming,
    string Outgoing,
    string Interfloor);

internal sealed record ReportCriteriaModel(
    string ActiveProfile,
    IReadOnlyList<ReportCriteriaProfileModel> Profiles,
    string FlowDefinition,
    string LegalNote);

internal sealed record ReportCriteriaProfileModel(
    string Name,
    ReportCriteriaThresholdModel ThreeStars,
    ReportCriteriaThresholdModel FourStars,
    ReportCriteriaThresholdModel FiveStars);

internal sealed record ReportCriteriaThresholdModel(int Hc5, int WtSeconds, int TtdSeconds);
