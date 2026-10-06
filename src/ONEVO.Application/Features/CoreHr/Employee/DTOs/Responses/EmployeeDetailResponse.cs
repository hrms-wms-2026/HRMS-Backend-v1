namespace ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

public record EmployeeDetailResponse(
    Guid Id,
    EmployeeDetailJobInformation JobInformation,
    EmployeeDetailPersonalInformation PersonalInformation,
    IReadOnlyList<EmployeeDetailEmergencyContact> EmergencyContacts,
    EmployeeDetailPayroll? Payroll,
    string? InvitationStatus,
    DateTimeOffset? InvitationExpiresAt,
    EmployeeListAttendanceSummaryResponse? AttendanceSummary = null,
    IReadOnlyList<EmployeeDetailProjectMembership>? ProjectMemberships = null);

public record EmployeeDetailJobInformation(
    string EmployeeNumber, Guid? LegalEntityId, string? LegalEntityName, string? DepartmentName, string? PositionName,
    Guid? PositionId, string? ReportingManagerName, string EmploymentTypeLabel, string Status,
    DateOnly HireDate, DateOnly? ProbationEndDate, string? WorkModeLabel,
    string EmploymentTypeCode, Guid? WorkModeId, DateOnly? TerminationDate, Guid? ReportingManagerId = null,
    string? Timezone = null);

public record EmployeeDetailPersonalInformation(
    string FirstName, string LastName, string Email, string? Phone, DateOnly? DateOfBirth,
    string? Gender, Guid? NationalityId, IReadOnlyList<EmployeeDetailAddress> Addresses);

public record EmployeeDetailAddress(Guid Id, string AddressType, string AddressJson, bool IsPrimary);

public record EmployeeDetailEmergencyContact(Guid Id, string Name, string Relationship, string Phone, string? Email, bool IsPrimary);

public record EmployeeDetailPayroll(bool HasBankDetailsOnFile, string? BankName, string? MaskedAccountNumber, string? AccountType);

public sealed record EmployeeBankDetailsRevealResponse(
    string BankName,
    string BranchName,
    string AccountHolderName,
    string AccountNumber,
    string AccountType,
    string? RoutingNumber);

/// <summary>A project the employee belongs to that the viewer shares (see IProjectMemberRepository.ListSharedProjectMembershipsAsync).</summary>
public record EmployeeDetailProjectMembership(Guid ProjectId, string ProjectName, DateTimeOffset MemberSince, bool IsActive);
