namespace ONEVO.Infrastructure.ExternalServices.Storage.CloudflareR2;

internal sealed record CloudflareR2CredentialBundle
{
    public string AccountId { get; init; } = string.Empty;
    public string BucketName { get; init; } = string.Empty;
    public string AccessKeyId { get; init; } = string.Empty;
    public string SecretAccessKey { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public string Region { get; init; } = "auto";
}
