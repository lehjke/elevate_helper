using System.Globalization;

namespace ElevateHelperWinUI.Services;

public sealed class LiftGroupRulesService
{
    private static readonly string[] CapacityOptions =
    [
        "320",
        "450",
        "550",
        "630",
        "825",
        "1050",
        "1350",
        "1600",
        "1800",
        "2025",
    ];

    private static readonly string[] SpeedOptions =
    [
        "1.0",
        "1.6",
        "1.75",
        "2.0",
        "2.5",
        "3.0",
        "4.0",
        "5.0",
        "6.0",
        "7.0",
        "8.0",
        "9.0",
        "10.0",
    ];

    private static readonly Dictionary<int, CabinDimensions> TypicalCabinDimensions = new()
    {
        [320] = new(1000, 1000),
        [450] = new(1100, 1200),
        [550] = new(1100, 1400),
        [630] = new(1100, 1400),
        [825] = new(1350, 1500),
        [1050] = new(1600, 1400),
        [1350] = new(1600, 2100),
        [1600] = new(1600, 2100),
        [1800] = new(1800, 2100),
        [2025] = new(1800, 2400),
    };

    private static readonly int[] DoorWidthOptions = Enumerable.Range(16, 13)
        .Select(index => 800 + ((index - 16) * 50))
        .ToArray();

    private static readonly Dictionary<int, MotionProfile> MotionProfiles = new()
    {
        [10] = new("0.900000", "1.000000"),
        [16] = new("0.900000", "1.000000"),
        [18] = new("0.900000", "1.000000"),
        [20] = new("0.900000", "1.000000"),
        [25] = new("0.900000", "1.000000"),
        [30] = new("1.100000", "1.500000"),
        [40] = new("1.100000", "1.500000"),
        [50] = new("1.100000", "1.500000"),
        [60] = new("1.100000", "1.500000"),
        [70] = new("1.200000", "1.800000"),
        [80] = new("1.200000", "1.800000"),
        [90] = new("1.200000", "1.800000"),
        [100] = new("1.200000", "1.800000"),
    };

    private static readonly Dictionary<(int WidthMm, DoorOpeningKind Kind), DoorProfile> DoorProfiles = new()
    {
        [(800, DoorOpeningKind.Telescopic)] = new("0.000000", "2.300000", "4.100000"),
        [(850, DoorOpeningKind.Telescopic)] = new("0.000000", "2.400000", "4.300000"),
        [(900, DoorOpeningKind.Telescopic)] = new("0.000000", "2.500000", "4.500000"),
        [(950, DoorOpeningKind.Telescopic)] = new("0.000000", "2.600000", "4.700000"),
        [(1000, DoorOpeningKind.Telescopic)] = new("0.000000", "2.600000", "4.900000"),
        [(1050, DoorOpeningKind.Telescopic)] = new("0.000000", "2.700000", "5.100000"),
        [(1100, DoorOpeningKind.Telescopic)] = new("0.000000", "2.800000", "5.300000"),
        [(1150, DoorOpeningKind.Telescopic)] = new("0.000000", "2.900000", "5.500000"),
        [(1200, DoorOpeningKind.Telescopic)] = new("0.000000", "2.900000", "5.700000"),
        [(1250, DoorOpeningKind.Telescopic)] = new("0.000000", "3.000000", "5.900000"),
        [(1300, DoorOpeningKind.Telescopic)] = new("0.000000", "3.100000", "6.000000"),
        [(1350, DoorOpeningKind.Telescopic)] = new("0.000000", "3.200000", "6.200000"),
        [(1400, DoorOpeningKind.Telescopic)] = new("0.000000", "3.300000", "6.400000"),
        [(800, DoorOpeningKind.Central)] = new("0.500000", "1.700000", "2.600000"),
        [(850, DoorOpeningKind.Central)] = new("0.500000", "1.700000", "2.700000"),
        [(900, DoorOpeningKind.Central)] = new("0.500000", "1.700000", "2.800000"),
        [(1000, DoorOpeningKind.Central)] = new("0.500000", "1.800000", "2.900000"),
        [(1050, DoorOpeningKind.Central)] = new("0.500000", "1.900000", "3.000000"),
        [(1100, DoorOpeningKind.Central)] = new("0.500000", "1.900000", "3.100000"),
        [(1150, DoorOpeningKind.Central)] = new("0.500000", "2.000000", "3.200000"),
        [(1200, DoorOpeningKind.Central)] = new("0.500000", "2.000000", "3.300000"),
        [(1250, DoorOpeningKind.Central)] = new("0.500000", "2.100000", "3.400000"),
        [(1300, DoorOpeningKind.Central)] = new("0.500000", "2.100000", "3.500000"),
        [(1350, DoorOpeningKind.Central)] = new("0.500000", "2.200000", "3.600000"),
        [(1400, DoorOpeningKind.Central)] = new("0.500000", "2.200000", "3.700000"),
    };

    public IReadOnlyList<string> GetCapacityOptions() => CapacityOptions;

    public IReadOnlyList<string> GetSpeedOptions() => SpeedOptions;

    public IReadOnlyList<int> GetDoorWidthOptions() => DoorWidthOptions;

    public IReadOnlyList<DoorOpeningKind> GetDoorOpeningOptions() =>
        [DoorOpeningKind.Central, DoorOpeningKind.Telescopic];

    public MotionProfile ResolveMotionProfile(string speedText)
    {
        if (!TryParseSpeedKey(speedText, out int speedKey))
        {
            return MotionProfiles[25];
        }

        return MotionProfiles.TryGetValue(speedKey, out MotionProfile? profile)
            ? profile
            : MotionProfiles[25];
    }

    public CabinDimensions ResolveCabinDimensions(string capacityText, string floorAreaText)
    {
        if (TryParseInt(capacityText, out int capacityKg) &&
            TypicalCabinDimensions.TryGetValue(capacityKg, out CabinDimensions? typicalDimensions))
        {
            return typicalDimensions;
        }

        if (TryParseDouble(floorAreaText, out double areaM2) && areaM2 > 0d)
        {
            int width = areaM2 >= 3.2d ? 1600 : areaM2 >= 2.5d ? 1400 : 1100;
            int depth = (int)(Math.Round((areaM2 * 1_000_000d) / width / 50d, MidpointRounding.AwayFromZero) * 50d);
            return new CabinDimensions(width, Math.Max(1000, depth));
        }

        return new CabinDimensions(1600, 2100);
    }

    public DoorProfile ResolveDoorProfile(int doorWidthMm, DoorOpeningKind openingKind)
    {
        return DoorProfiles.TryGetValue((doorWidthMm, openingKind), out DoorProfile? profile)
            ? profile
            : DoorProfiles[(1000, DoorOpeningKind.Central)];
    }

    public bool TryResolveDoorSpecification(
        string? doorPreOpeningText,
        string? doorOpenTimeText,
        string? doorCloseTimeText,
        out int doorWidthMm,
        out DoorOpeningKind openingKind)
    {
        doorWidthMm = 1000;
        openingKind = DoorOpeningKind.Central;

        if (!TryParseDouble(doorOpenTimeText, out double doorOpenTime) ||
            !TryParseDouble(doorCloseTimeText, out double doorCloseTime))
        {
            return false;
        }

        TryParseDouble(doorPreOpeningText, out double doorPreOpening);

        KeyValuePair<(int WidthMm, DoorOpeningKind Kind), DoorProfile>? exactMatch = DoorProfiles
            .FirstOrDefault(entry =>
                NearlyEquals(entry.Value.DoorPreOpening, doorPreOpening) &&
                NearlyEquals(entry.Value.DoorOpenTime, doorOpenTime) &&
                NearlyEquals(entry.Value.DoorCloseTime, doorCloseTime));

        if (!exactMatch.Equals(default(KeyValuePair<(int WidthMm, DoorOpeningKind Kind), DoorProfile>)))
        {
            doorWidthMm = exactMatch.Value.Key.WidthMm;
            openingKind = exactMatch.Value.Key.Kind;
            return true;
        }

        KeyValuePair<(int WidthMm, DoorOpeningKind Kind), DoorProfile> nearestMatch = DoorProfiles
            .OrderBy(entry => ScoreDoorProfile(entry.Value, doorPreOpening, doorOpenTime, doorCloseTime))
            .First();

        doorWidthMm = nearestMatch.Key.WidthMm;
        openingKind = nearestMatch.Key.Kind;
        return true;
    }

    public double ResolveCarAreaSquareMeters(int cabinWidthMm, int cabinDepthMm)
    {
        return Math.Round((cabinWidthMm * cabinDepthMm) / 1_000_000d, 6, MidpointRounding.AwayFromZero);
    }

    public DoorOpeningKind ResolveDoorOpeningKind(string? doorType)
    {
        if (string.Equals(doorType, "–¶–û", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(doorType, "÷Œ", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(doorType, "Central", StringComparison.OrdinalIgnoreCase))
        {
            return DoorOpeningKind.Central;
        }

        return DoorOpeningKind.Telescopic;
    }

    private static bool TryParseSpeedKey(string speedText, out int speedKey)
    {
        speedKey = 25;
        if (!double.TryParse(speedText, NumberStyles.Float, CultureInfo.InvariantCulture, out double speed) &&
            !double.TryParse(speedText, NumberStyles.Float, CultureInfo.GetCultureInfo("ru-RU"), out speed))
        {
            return false;
        }

        speedKey = (int)Math.Round(speed * 10d, MidpointRounding.AwayFromZero);
        return true;
    }

    private static bool TryParseDouble(string? text, out double value)
    {
        value = 0d;
        return !string.IsNullOrWhiteSpace(text) &&
            (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
             double.TryParse(text, NumberStyles.Float, CultureInfo.GetCultureInfo("ru-RU"), out value));
    }

    private static bool TryParseInt(string? text, out int value)
    {
        value = 0;
        return !string.IsNullOrWhiteSpace(text) &&
            (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ||
             int.TryParse(text, NumberStyles.Integer, CultureInfo.GetCultureInfo("ru-RU"), out value));
    }

    private static bool NearlyEquals(string expectedText, double actualValue)
    {
        return TryParseDouble(expectedText, out double expectedValue) &&
               Math.Abs(expectedValue - actualValue) <= 0.051d;
    }

    private static double ScoreDoorProfile(DoorProfile profile, double preOpen, double open, double close)
    {
        TryParseDouble(profile.DoorPreOpening, out double expectedPreOpen);
        TryParseDouble(profile.DoorOpenTime, out double expectedOpen);
        TryParseDouble(profile.DoorCloseTime, out double expectedClose);
        return Math.Abs(expectedPreOpen - preOpen) +
               Math.Abs(expectedOpen - open) +
               Math.Abs(expectedClose - close);
    }
}

public enum DoorOpeningKind
{
    Central,
    Telescopic,
}

public sealed record MotionProfile(string Acceleration, string Jerk);

public sealed record DoorProfile(string DoorPreOpening, string DoorOpenTime, string DoorCloseTime);

public sealed record CabinDimensions(int WidthMm, int DepthMm);
