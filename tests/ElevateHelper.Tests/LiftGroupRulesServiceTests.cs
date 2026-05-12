using ElevateHelperWinUI.Services;

namespace ElevateHelper.Tests;

public sealed class LiftGroupRulesServiceTests
{
    private readonly LiftGroupRulesService service = new();

    [Theory]
    [InlineData("1.0", "0.900000", "1.000000")]
    [InlineData("1.75", "0.900000", "1.000000")]
    [InlineData("4.0", "1.100000", "1.500000")]
    [InlineData("8.0", "1.200000", "1.800000")]
    public void ResolveMotionProfile_ReturnsExpectedBand(string speed, string expectedAcceleration, string expectedJerk)
    {
        MotionProfile profile = service.ResolveMotionProfile(speed);

        Assert.Equal(expectedAcceleration, profile.Acceleration);
        Assert.Equal(expectedJerk, profile.Jerk);
    }

    [Theory]
    [InlineData(1000, DoorOpeningKind.Central, "0.500000", "1.800000", "2.900000")]
    [InlineData(1200, DoorOpeningKind.Central, "0.500000", "2.000000", "3.300000")]
    [InlineData(1000, DoorOpeningKind.Telescopic, "0.000000", "2.600000", "4.900000")]
    [InlineData(1400, DoorOpeningKind.Telescopic, "0.000000", "3.300000", "6.400000")]
    public void ResolveDoorProfile_ReturnsExpectedTimings(
        int width,
        DoorOpeningKind kind,
        string expectedPreOpening,
        string expectedOpen,
        string expectedClose)
    {
        DoorProfile profile = service.ResolveDoorProfile(width, kind);

        Assert.Equal(expectedPreOpening, profile.DoorPreOpening);
        Assert.Equal(expectedOpen, profile.DoorOpenTime);
        Assert.Equal(expectedClose, profile.DoorCloseTime);
    }

    [Fact]
    public void ResolveCarAreaSquareMeters_ConvertsMillimetersToSquareMeters()
    {
        double area = service.ResolveCarAreaSquareMeters(1600, 2100);

        Assert.Equal(3.36d, area);
    }
}
