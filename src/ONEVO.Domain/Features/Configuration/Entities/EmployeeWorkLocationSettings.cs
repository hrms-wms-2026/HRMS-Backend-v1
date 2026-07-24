using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Configuration.Entities;

public class EmployeeWorkLocationSettings : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public string WorkMode { get; set; } = "onsite"; // onsite | remote | hybrid | field
    public bool WorkLocationVerificationEnabled { get; set; } = true;
    public int? GracePeriodMinutes { get; set; }
    public bool PhotoChallengeOnMismatch { get; set; } = true;
    public Guid SetById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
