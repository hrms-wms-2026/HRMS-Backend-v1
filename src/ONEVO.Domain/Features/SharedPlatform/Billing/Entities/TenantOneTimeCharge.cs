using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.SharedPlatform.Entities;

public class TenantOneTimeCharge : BaseEntity
{
    public string SetupOptionKey { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    // Status: draft, approved, invoiced, paid, void
    public string Status { get; set; } = "draft";
    public bool ChargedOnce { get; set; } = true;
}
