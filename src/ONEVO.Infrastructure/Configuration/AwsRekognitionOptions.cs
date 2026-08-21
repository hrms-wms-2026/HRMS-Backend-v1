using System.ComponentModel.DataAnnotations;

namespace ONEVO.Infrastructure.Configuration;

public class AwsRekognitionOptions
{
    public const string SectionName = "AwsRekognition";

    [Required] public string Region { get; set; } = string.Empty;
    [Required] public string LivenessRoleArn { get; set; } = string.Empty;

    /// <summary>Minimum GetFaceLivenessSessionResults confidence (0-100) to accept enrollment.</summary>
    [Range(0, 100)] public float LivenessConfidenceThreshold { get; set; } = 90f;

    /// <summary>STS AssumeRole session duration; AWS minimum is 900 seconds.</summary>
    [Range(900, 3600)] public int RoleSessionDurationSeconds { get; set; } = 900;

    /// <summary>A CreateFaceLivenessSession attempt older than this is treated as expired on Complete.</summary>
    [Range(1, 30)] public int SessionTtlMinutes { get; set; } = 3;
}
