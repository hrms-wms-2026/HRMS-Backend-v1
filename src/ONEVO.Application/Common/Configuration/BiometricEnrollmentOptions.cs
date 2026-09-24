namespace ONEVO.Application.Common.Configuration;

/// <summary>
/// Handler-facing enrollment thresholds bound from the same AwsRekognition config section.
/// </summary>
public class BiometricEnrollmentOptions
{
    public const string SectionName = "AwsRekognition";

    public float LivenessConfidenceThreshold { get; set; } = 90f;
    public int SessionTtlMinutes { get; set; } = 3;
}
