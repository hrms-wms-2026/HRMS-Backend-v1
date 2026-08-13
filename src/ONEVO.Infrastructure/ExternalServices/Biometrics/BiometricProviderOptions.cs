namespace ONEVO.Infrastructure.ExternalServices.Biometrics;

/// <summary>
/// Bound from the Biometrics:* section. NON-SECRET settings only — AWS credentials come from
/// the ambient IAM role on backend compute (design decision: IAM role, not static keys), and the
/// STS-assumed capture-client credentials are short-lived and never persisted here or anywhere else.
/// </summary>
public class BiometricProviderOptions
{
    public const string SectionName = "Biometrics";

    public string Region { get; set; } = "ap-south-1";
    public string CaptureRoleArn { get; set; } = string.Empty;
    public string KmsKeyId { get; set; } = string.Empty;
    public string DefaultChallengeType { get; set; } = "FaceMovementAndLightChallenge";
    public int CaptureCredentialsDurationSeconds { get; set; } = 900; // 15 minutes, per design doc
}
