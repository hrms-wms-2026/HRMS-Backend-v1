using ONEVO.Application.Common.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Common.Helpers;

public sealed class GeoDistanceCalculatorTests
{
    [Fact]
    public void DistanceMeters_SamePoint_ReturnsZero()
    {
        var distance = GeoDistanceCalculator.DistanceMeters(6.9271, 79.8612, 6.9271, 79.8612);

        Assert.Equal(0, distance, precision: 6);
    }

    [Fact]
    public void DistanceMeters_KnownPoints_MatchesExpectedDistanceWithinTolerance()
    {
        // Colombo Fort <-> Colombo World Trade Center, ~600m apart per public map measurements.
        var distance = GeoDistanceCalculator.DistanceMeters(6.9344, 79.8428, 6.9271, 79.8478);

        Assert.InRange(distance, 500, 1200);
    }

    [Fact]
    public void DistanceMeters_IsSymmetric()
    {
        var a = GeoDistanceCalculator.DistanceMeters(6.9271, 79.8612, 6.8500, 79.8800);
        var b = GeoDistanceCalculator.DistanceMeters(6.8500, 79.8800, 6.9271, 79.8612);

        Assert.Equal(a, b, precision: 9);
    }

    [Fact]
    public void DistanceMeters_AntipodalPoints_ReturnsHalfEarthCircumference()
    {
        var distance = GeoDistanceCalculator.DistanceMeters(0, 0, 0, 180);

        Assert.InRange(distance, 20_000_000, 20_040_000);
    }
}
