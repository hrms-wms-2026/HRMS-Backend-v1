namespace ONEVO.Domain.Features.DevPlatform.Compliance.Entities;

public class LegalDocumentVersion
{
    public Guid Id { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? ContentUrl { get; set; }
    public bool IsRequired { get; set; }
    public string BlockScope { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? PublishedById { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? PublishReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
