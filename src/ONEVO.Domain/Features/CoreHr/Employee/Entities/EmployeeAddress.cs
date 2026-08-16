using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.CoreHr.Entities;

public class EmployeeAddress : BaseEntity
{
    public Guid EmployeeId { get; set; }
    public string AddressType { get; set; } = string.Empty; // "permanent" | "current"
    public string AddressJson { get; set; } = "{}";
    public bool IsPrimary { get; set; }
}
