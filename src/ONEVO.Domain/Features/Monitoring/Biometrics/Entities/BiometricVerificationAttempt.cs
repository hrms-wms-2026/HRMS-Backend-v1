using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

public class BiometricVerificationAttempt : ITenantOwnedEntity
{
    private static readonly Dictionary<string, string[]> AllowedTransitions = new()
    {
        [BiometricAttemptStatus.Created]   = [BiometricAttemptStatus.Capturing, BiometricAttemptStatus.Expired],
        [BiometricAttemptStatus.Capturing] = [BiometricAttemptStatus.Verifying, BiometricAttemptStatus.Expired, BiometricAttemptStatus.ProviderError],
        [BiometricAttemptStatus.Verifying] = [BiometricAttemptStatus.Verified, BiometricAttemptStatus.Rejected, BiometricAttemptStatus.ProviderError, BiometricAttemptStatus.Expired],
        [BiometricAttemptStatus.Verified]     = [],
        [BiometricAttemptStatus.Rejected]     = [],
        [BiometricAttemptStatus.ProviderError] = [],
        [BiometricAttemptStatus.Expired]      = []
    };

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid UserId { get; set; }
    public Guid DeviceRegistrationId { get; set; }

    public string Purpose { get; set; } = BiometricAttemptPurpose.Enrollment;

    /// <summary>Set only when Purpose == CheckIn (Plan 2). Null for enrollment attempts.</summary>
    public Guid? AttendanceSessionId { get; set; }

    public string? AwsSessionId { get; set; }
    public string AwsRegion { get; set; } = "ap-south-1";
    public string ChallengeType { get; set; } = "FaceMovementAndLightChallenge";
    public DateTimeOffset? AwsSessionExpiresAt { get; set; }

    public string Status { get; set; } = BiometricAttemptStatus.Created;
    public double? LivenessConfidence { get; set; }
    public double? MatchConfidence { get; set; }
    public string? FailureCode { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Enforces the Created -> Capturing -> Verifying -> {Verified|Rejected|ProviderError}
    /// graph (plus Expired from any non-terminal state). Mirrors the Tray-side
    /// AgentStateMachine's validated-transition pattern so an invalid handler code path
    /// fails loudly instead of silently corrupting attempt state.
    /// </summary>
    public bool TryTransition(string target, out string previous)
    {
        previous = Status;
        if (!AllowedTransitions.TryGetValue(Status, out var allowed) || !allowed.Contains(target))
            return false;

        Status = target;
        return true;
    }
}
