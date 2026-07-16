namespace ONEVO.Application.Common.Configuration;

/// <summary>
/// Bound from configuration section <c>GoogleAuth</c>. Lives in Application so
/// command handlers can consume it without referencing Infrastructure.
/// </summary>
public class GoogleAuthOptions
{
    public const string SectionName = "GoogleAuth";
    public GoogleClientOptions Admin { get; set; } = new();
    public GoogleClientOptions TenantInvite { get; set; } = new();
}

public class GoogleClientOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}
