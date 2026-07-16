using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.CoreHr.Entities;

public class Employee : BaseEntity
{
    public Guid UserId { get; set; }
    public string EmployeeNumber { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public Guid? NationalityId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? JobTitleId { get; set; }
    public Guid? ManagerId { get; set; }
    public Guid? LegalEntityId { get; set; }
    public int EmploymentTypeId { get; set; } = 1;
    public int EmploymentStatusId { get; set; } = 1;
    public int WorkModeId { get; set; } = 1;
    public DateOnly HireDate { get; set; }
    public DateOnly? ProbationEndDate { get; set; }
    public DateOnly? TerminationDate { get; set; }
    public Guid? AvatarFileId { get; set; }
}
