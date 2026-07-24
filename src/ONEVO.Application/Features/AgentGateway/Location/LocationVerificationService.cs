namespace ONEVO.Application.Features.AgentGateway.Location;

public sealed class LocationVerificationService : ILocationVerificationService
{
    private const double EarthRadiusMeters = 6_371_000d;
    private const decimal MaximumAccuracyMeters = 250m;
    private static readonly TimeSpan MaximumCaptureAge = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaximumFutureSkew = TimeSpan.FromSeconds(30);

    public LocationMatchResult ValidateCapture(
        LocationCapture capture,
        DateTimeOffset serverNow)
    {
        if (!string.Equals(capture.PermissionState, "granted", StringComparison.OrdinalIgnoreCase))
            return Invalid("permission_denied");

        if (!CoordinatesAreValid(capture.Latitude, capture.Longitude))
            return Invalid("invalid_coordinates");

        if (capture.AccuracyMeters <= 0)
            return Invalid("invalid_accuracy");

        if (capture.AccuracyMeters > MaximumAccuracyMeters)
            return Invalid("accuracy_too_low");

        if (capture.CapturedAt > serverNow.Add(MaximumFutureSkew))
            return Invalid("capture_in_future");

        if (serverNow - capture.CapturedAt > MaximumCaptureAge)
            return Invalid("capture_stale");

        return new LocationMatchResult(true, true, 0m, string.Empty);
    }

    public LocationMatchResult Evaluate(
        LocationCapture capture,
        LocationTarget target,
        DateTimeOffset serverNow)
    {
        var validation = ValidateCapture(capture, serverNow);
        if (!validation.IsValid)
            return validation;

        if (!CoordinatesAreValid(target.Latitude, target.Longitude))
            return Invalid("invalid_coordinates");

        if (target.AllowedRadiusMeters is < 25 or > 50_000)
            return Invalid("invalid_radius");

        var distance = CalculateDistanceMeters(
            capture.Latitude,
            capture.Longitude,
            target.Latitude,
            target.Longitude);
        var isMatch = distance <= target.AllowedRadiusMeters + capture.AccuracyMeters;

        return new LocationMatchResult(
            IsValid: true,
            IsMatch: isMatch,
            DistanceMeters: distance,
            FailureCode: isMatch ? string.Empty : "outside_allowed_radius");
    }

    private static bool CoordinatesAreValid(decimal latitude, decimal longitude) =>
        latitude is >= -90m and <= 90m &&
        longitude is >= -180m and <= 180m;

    private static decimal CalculateDistanceMeters(
        decimal latitude1,
        decimal longitude1,
        decimal latitude2,
        decimal longitude2)
    {
        static double ToRadians(decimal degrees) => (double)degrees * Math.PI / 180d;

        var lat1 = ToRadians(latitude1);
        var lat2 = ToRadians(latitude2);
        var deltaLatitude = lat2 - lat1;
        var deltaLongitude = ToRadians(longitude2 - longitude1);

        var sinLatitude = Math.Sin(deltaLatitude / 2d);
        var sinLongitude = Math.Sin(deltaLongitude / 2d);
        var haversine =
            sinLatitude * sinLatitude +
            Math.Cos(lat1) * Math.Cos(lat2) * sinLongitude * sinLongitude;
        var centralAngle = 2d * Math.Atan2(Math.Sqrt(haversine), Math.Sqrt(1d - haversine));

        return (decimal)(EarthRadiusMeters * centralAngle);
    }

    private static LocationMatchResult Invalid(string failureCode) =>
        new(false, false, null, failureCode);
}
