using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.CoreHr.Entities;

public class EmployeeBankDetail : BaseEntity
{
    public Guid EmployeeId { get; set; }
    public string BankName { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string AccountHolderName { get; set; } = string.Empty;
    public string AccountNumberEncrypted { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public string? RoutingNumber { get; set; }
    public bool IsPrimary { get; set; }
}
