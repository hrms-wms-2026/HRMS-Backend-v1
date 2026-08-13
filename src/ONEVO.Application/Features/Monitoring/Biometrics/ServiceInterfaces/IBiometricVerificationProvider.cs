namespace ONEVO.Application.Features.Monitoring.Biometrics.ServiceInterfaces;

public interface IBiometricVerificationProvider
{
    /// <summary>Control-plane call: creates a new AWS Face Liveness session. Returns the AWS session id.</summary>
    Task<FaceLivenessSessionCreated> CreateLivenessSessionAsync(
        CreateLivenessSessionRequest request, CancellationToken ct);

    /// <summary>Fetches the outcome of a completed (or in-progress) liveness session.</summary>
    Task<FaceLivenessSessionResult> GetLivenessSessionResultAsync(
        string awsSessionId, CancellationToken ct);

    /// <summary>Compares two face images (daily check-in reference vs. stored enrollment reference — used starting Plan 2).</summary>
    Task<FaceMatchResult> CompareFacesAsync(
        byte[] sourceImageBytes, byte[] targetImageBytes, CancellationToken ct);

    /// <summary>
    /// Issues short-lived (max 15 min) AWS credentials scoped to rekognition:StartFaceLivenessSession
    /// only, for the WebView2 capture client. Never persisted — caller must keep these in memory only.
    /// </summary>
    Task<ScopedCaptureCredentials> IssueScopedCaptureCredentialsAsync(
        string awsSessionId, CancellationToken ct);
}

public sealed record CreateLivenessSessionRequest(string ChallengeType, string KmsKeyId);

public sealed record FaceLivenessSessionCreated(string AwsSessionId);

/// <summary>Status: "CREATED" | "IN_PROGRESS" | "SUCCEEDED" | "FAILED" | "EXPIRED" (AWS's own status strings).</summary>
public sealed record FaceLivenessSessionResult(
    string Status,
    double? Confidence,
    byte[]? ReferenceImageBytes);

public sealed record FaceMatchResult(bool IsMatch, double Similarity);

public sealed record ScopedCaptureCredentials(
    string AccessKeyId,
    string SecretAccessKey,
    string SessionToken,
    string Region,
    DateTimeOffset Expiration);
