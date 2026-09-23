namespace ONEVO.Application.Common.Helpers;

/// <summary>Great-circle distance between two lat/lng points via the Haversine formula. Used by the
/// on-site work-location warning to compare a captured check-in against a legal entity's configured
/// office point - accurate enough for a several-hundred-metre geofence radius; not for surveying.</summary>
public static class GeoDistanceCalculator
{
    private const double EarthRadiusMeters = 6_371_000;

    public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);
        var rLat1 = DegreesToRadians(lat1);
        var rLat2 = DegreesToRadians(lat2);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(rLat1) * Math.Cos(rLat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return EarthRadiusMeters * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;
}
