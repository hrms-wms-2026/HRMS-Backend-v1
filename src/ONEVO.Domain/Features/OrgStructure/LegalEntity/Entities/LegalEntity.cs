using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.OrgStructure.Entities;

public class LegalEntity : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? RegistrationNumber { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = string.Empty;
    public string? AddressJson { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsPrimary { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
