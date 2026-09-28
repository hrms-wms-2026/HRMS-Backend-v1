namespace ONEVO.Application.Common.ServiceInterfaces;

/// <param name="FaceCount">Faces counted as people in the frame (tiny/low-confidence background faces excluded).</param>
/// <param name="Yaw">Head yaw in degrees; the sign is not reliable as left/right, only the magnitude.</param>
/// <param name="FacingFront">Head is turned little enough for the face-setup "look straight" photo.</param>
/// <param name="GlassesGlare">Glasses on and the eyes cannot be seen — light reflecting on the lenses. Also clears NoSunglassesOrMask.</param>
/// <param name="Faces">Every face DetectFaces reported (including ignored background ones), for diagnosis.</param>
/// <param name="EyesClosed">No glasses and the eyes are not open. Also clears FaceVisible.</param>
/// <param name="TurnedSideways">Head is turned enough for a face-setup side photo, but still inside the visible-face limit.</param>
public record FaceQualityOutcome(
    bool LightingOk,
    bool FaceVisible,
    bool NoSunglassesOrMask,
    float? Brightness,
    float? FaceConfidence,
    int FaceCount = 1,
    float? Yaw = null,
    bool FacingFront = false,
    bool TurnedSideways = false,
    bool GlassesGlare = false,
    bool EyesClosed = false,
    IReadOnlyList<DetectedFace>? Faces = null);

/// <summary>One face DetectFaces reported; box values are fractions (0-1) of the image.</summary>
public record DetectedFace(float Confidence, float Left, float Top, float Width, float Height);

public interface IFaceQualityService
{
    /// <summary>
    /// Inspects a captured photo for lighting, a single clearly visible face, and
    /// sunglasses/mask occlusion. The stream must be readable from position 0; it is not disposed.
    /// </summary>
    Task<FaceQualityOutcome> AnalyzeAsync(Stream image, CancellationToken ct);
}
