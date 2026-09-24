using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.CoreHr.Entities;

public class EmployeeDependent : BaseEntity
{
    public Guid EmployeeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty; // spouse | child | parent | other
    public DateOnly DateOfBirth { get; set; }
    public bool IsEmergencyContact { get; set; }
    public string? Phone { get; set; }
}
