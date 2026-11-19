using System.ComponentModel.DataAnnotations;

namespace ONEVO.Infrastructure.Configuration;

public class AwsRekognitionOptions
{
    public const string SectionName = "AwsRekognition";

    /// <summary>Fallback region when the aws_rekognition platform service key JSON omits region. Access keys never live here.</summary>
    [Required] public string Region { get; set; } = string.Empty;

    /// <summary>Fallback liveness role when the platform service key JSON omits livenessRoleArn. Access keys never live here.</summary>
    [Required] public string LivenessRoleArn { get; set; } = string.Empty;

    /// <summary>Minimum GetFaceLivenessSessionResults confidence (0-100) to accept enrollment.</summary>
    [Range(0, 100)] public float LivenessConfidenceThreshold { get; set; } = 90f;

    /// <summary>Minimum CompareFaces similarity (0-100) to accept a check-in face match.</summary>
    [Range(0, 100)] public float FaceMatchSimilarityThreshold { get; set; } = 80f;

    /// <summary>Minimum DetectFaces Quality.Brightness (0-100) for "Good lighting".</summary>
    [Range(0, 100)] public float MinBrightness { get; set; } = 40f;

    /// <summary>Maximum DetectFaces Quality.Brightness (0-100); blown-out frames fail lighting.</summary>
    [Range(0, 100)] public float MaxBrightness { get; set; } = 95f;

    /// <summary>Minimum DetectFaces face Confidence (0-100) for "Face clearly visible".</summary>
    [Range(0, 100)] public float MinFaceConfidence { get; set; } = 90f;

    /// <summary>Minimum BoundingBox width/height as a fraction of the image (0-1).</summary>
    [Range(0, 1)] public float MinFaceBoxRatio { get; set; } = 0.12f;

    /// <summary>Maximum absolute yaw/pitch in degrees before the face is treated as turned away.</summary>
    [Range(0, 90)] public float MaxHeadPoseDegrees { get; set; } = 35f;

    /// <summary>Minimum DetectFaces confidence (0-100) that the eyes are closed before a photo fails as closed eyes / glasses glare.</summary>
    [Range(0, 100)] public float MinEyesClosedConfidence { get; set; } = 90f;

    /// <summary>Face setup "look straight" photo: maximum absolute yaw in degrees.</summary>
    [Range(0, 90)] public float FrontMaxYawDegrees { get; set; } = 15f;

    /// <summary>Face setup left/right photos: minimum absolute yaw in degrees (must be clearly turned).</summary>
    [Range(0, 90)] public float SideMinYawDegrees { get; set; } = 12f;

    /// <summary>Minimum Sunglasses/FaceOccluded confidence (0-100) before treating the flag as set.</summary>
    [Range(0, 100)] public float AccessoryConfidenceThreshold { get; set; } = 80f;

    /// <summary>STS AssumeRole session duration; AWS minimum is 900 seconds.</summary>
    [Range(900, 3600)] public int RoleSessionDurationSeconds { get; set; } = 900;

    /// <summary>A CreateFaceLivenessSession attempt older than this is treated as expired on Complete.</summary>
    [Range(1, 30)] public int SessionTtlMinutes { get; set; } = 3;
}
