using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.CoreHr.Entities;

public class EmployeeEmergencyContact : BaseEntity
{
    public Guid EmployeeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool IsPrimary { get; set; }
}
