using ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

namespace ONEVO.Application.Features.Monitoring.Biometrics.ServiceInterfaces;

/// <param name="HasReference">The profile has at least one enrolled reference photo.</param>
/// <param name="IsMatch">The captured face matched one of the references.</param>
/// <param name="Similarity">Best CompareFaces similarity seen (null when nothing could be compared).</param>
/// <param name="Failed">References exist but none could be read or compared (storage/AWS failure).</param>
public record EnrolledFaceMatch(bool HasReference, bool IsMatch, float? Similarity, bool Failed);

public interface IEnrolledFaceMatcher
{
    /// <summary>
    /// Compares a captured face against the employee's enrolled references: the front photo
    /// first, then the left/right photos from tray face setup, stopping at the first match.
    /// Single-photo profiles (enrolled before side photos existed) compare against front only.
    /// </summary>
    Task<EnrolledFaceMatch> MatchAsync(
        Guid tenantId, BiometricProfile? profile, Stream captured, CancellationToken ct);
}
