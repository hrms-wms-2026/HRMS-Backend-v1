namespace ONEVO.Domain.Features.Monitoring.Biometrics.Entities;

public static class BiometricAttemptStatus
{
    public const string Created      = "created";
    public const string Capturing    = "capturing";
    public const string Verifying    = "verifying";
    public const string Verified     = "verified";
    public const string Rejected     = "rejected";
    public const string ProviderError = "provider_error";
    public const string Expired      = "expired";
}
