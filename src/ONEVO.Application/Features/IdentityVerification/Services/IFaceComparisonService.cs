namespace ONEVO.Application.Features.IdentityVerification.Services;

public interface IFaceComparisonService
{
    Task<FaceDetectionResult> DetectFacesAsync(
        byte[] imageBytes,
        CancellationToken ct);

    Task<FaceComparisonResult> CompareFacesAsync(
        byte[] referenceImageBytes,
        byte[] candidateImageBytes,
        decimal threshold,
        CancellationToken ct);
}

public sealed record FaceDetectionResult(
    bool ProviderAvailable,
    int FaceCount,
    string? FailureCode = null);

public sealed record FaceComparisonResult(
    bool ProviderAvailable,
    decimal Similarity,
    string? FailureCode = null);

