using ONEVO.Application.Features.AgentGateway.Location;
using Xunit;

namespace ONEVO.Tests.Unit.Features.AgentGateway;

public class LocationVerificationServiceTests
{
    private readonly LocationVerificationService _service = new();
    private readonly DateTimeOffset _now = new(2026, 7, 25, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Evaluate_ExactCoordinates_Accepts()
    {
        var result = _service.Evaluate(
            Capture(6.927079m, 79.861244m),
            Target(6.927079m, 79.861244m, 100),
            _now);

        Assert.True(result.IsValid);
        Assert.True(result.IsMatch);
        Assert.Equal(string.Empty, result.FailureCode);
        Assert.InRange(result.DistanceMeters!.Value, 0m, 0.5m);
    }

    [Fact]
    public void Evaluate_OutsideRadius_ReturnsMeasuredMismatch()
    {
        var result = _service.Evaluate(
            Capture(0m, 1m, accuracyMeters: 5m),
            Target(0m, 0m, 50_000),
            _now);

        Assert.True(result.IsValid);
        Assert.False(result.IsMatch);
        Assert.Equal("outside_allowed_radius", result.FailureCode);
        Assert.InRange(result.DistanceMeters!.Value, 111_000m, 111_300m);
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    [InlineData(0, 181)]
    [InlineData(0, -181)]
    public void Evaluate_InvalidCoordinates_FailsClosed(double latitude, double longitude)
    {
        var result = _service.Evaluate(
            Capture((decimal)latitude, (decimal)longitude),
            Target(0m, 0m, 100),
            _now);

        Assert.False(result.IsValid);
        Assert.False(result.IsMatch);
        Assert.Equal("invalid_coordinates", result.FailureCode);
    }

    [Fact]
    public void Evaluate_PermissionDenied_FailsClosed()
    {
        var result = _service.Evaluate(
            Capture(0m, 0m, permissionState: "denied"),
            Target(0m, 0m, 100),
            _now);

        Assert.False(result.IsValid);
        Assert.Equal("permission_denied", result.FailureCode);
    }

    [Fact]
    public void Evaluate_StaleCapture_FailsClosed()
    {
        var result = _service.Evaluate(
            Capture(0m, 0m, capturedAt: _now.AddMinutes(-2).AddSeconds(-1)),
            Target(0m, 0m, 100),
            _now);

        Assert.False(result.IsValid);
        Assert.Equal("capture_stale", result.FailureCode);
    }

    [Fact]
    public void Evaluate_ImplausiblyInaccurateCapture_FailsClosed()
    {
        var result = _service.Evaluate(
            Capture(0m, 0m, accuracyMeters: 251m),
            Target(0m, 0m, 100),
            _now);

        Assert.False(result.IsValid);
        Assert.Equal("accuracy_too_low", result.FailureCode);
    }

    [Theory]
    [InlineData(24)]
    [InlineData(50001)]
    public void Evaluate_InvalidRadius_FailsClosed(int radius)
    {
        var result = _service.Evaluate(
            Capture(0m, 0m),
            Target(0m, 0m, radius),
            _now);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_radius", result.FailureCode);
    }

    private LocationCapture Capture(
        decimal latitude,
        decimal longitude,
        decimal accuracyMeters = 10m,
        DateTimeOffset? capturedAt = null,
        string permissionState = "granted") =>
        new(latitude, longitude, accuracyMeters, capturedAt ?? _now, permissionState);

    private static LocationTarget Target(
        decimal latitude,
        decimal longitude,
        int radius) =>
        new(Guid.NewGuid(), "company_office", latitude, longitude, radius);
}
