namespace ONEVO.Application.Common.ServiceInterfaces;

public record FaceQualityOutcome(
    bool LightingOk,
    bool FaceVisible,
    bool NoSunglassesOrMask,
    float? Brightness,
    float? FaceConfidence,
    int FaceCount = 1);

public interface IFaceQualityService
{
    /// <summary>
    /// Inspects a captured photo for lighting, a single clearly visible face, and
    /// sunglasses/mask occlusion. The stream must be readable from position 0; it is not disposed.
    /// </summary>
    Task<FaceQualityOutcome> AnalyzeAsync(Stream image, CancellationToken ct);
}
