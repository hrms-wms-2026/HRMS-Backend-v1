namespace ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

public record MyPersonalInformationResponse(
    string FirstName, string LastName, string Email, string? Phone,
    DateOnly? DateOfBirth, string? Gender, Guid? NationalityId, string? CountryName,
    string? DisplayTimezone, string? AvatarUrl,
    IReadOnlyList<MyAddressResponse> Addresses, string Version);

public record MyAddressResponse(Guid Id, string AddressType, string AddressJson, bool IsPrimary);

public record MyJobInformationResponse(
    string EmployeeNumber, string? LegalEntityName, string? DepartmentName, string? PositionName,
    string? ReportingManagerName, string EmploymentTypeLabel, string EmploymentStatus,
    DateOnly HireDate, DateOnly? ProbationEndDate, string WorkMode);

public record MyEmergencyContactResponse(Guid Id, string Name, string Relationship, string Phone, string? Email, bool IsPrimary);

public record MyDependentResponse(Guid Id, string Name, string Relationship, DateOnly DateOfBirth, bool IsEmergencyContact, string? Phone);

public record MyPayrollResponse(bool HasBankDetailsOnFile, string? BankName, string? MaskedAccountNumber, string? AccountType, bool CanEdit);

public record MySecurityResponse(bool MfaEnabled, DateTimeOffset? LastPasswordChangedAt);

public record MyProfileResponse(
    MyPersonalInformationResponse PersonalInformation,
    MyJobInformationResponse JobInformation,
    IReadOnlyList<MyEmergencyContactResponse> EmergencyContacts,
    IReadOnlyList<MyDependentResponse> Dependents,
    MyPayrollResponse Payroll,
    MySecurityResponse Security);
