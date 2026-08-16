using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDrafts.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetMyProfile;

public class GetMyProfileQueryHandler : IRequestHandler<GetMyProfileQuery, Result<MyProfileResponse>>
{
    private readonly Common.RepositoryInterfaces.IEmployeeRepository _commonEmployees;
    private readonly IEmployeeRepository _featureEmployees;
    private readonly IEmployeeProfileRepository _profile;
    private readonly IWorkModeRepository _workModes;
    private readonly IEncryptionService _encryption;
    private readonly ICurrentUser _currentUser;

    public GetMyProfileQueryHandler(
        Common.RepositoryInterfaces.IEmployeeRepository commonEmployees,
        IEmployeeRepository featureEmployees,
        IEmployeeProfileRepository profile,
        IWorkModeRepository workModes,
        IEncryptionService encryption,
        ICurrentUser currentUser)
    {
        _commonEmployees = commonEmployees;
        _featureEmployees = featureEmployees;
        _profile = profile;
        _workModes = workModes;
        _encryption = encryption;
        _currentUser = currentUser;
    }

    public async Task<Result<MyProfileResponse>> Handle(GetMyProfileQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<MyProfileResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var employee = await _commonEmployees.GetByUserIdAsync(tenantId, _currentUser.UserId, ct);
        if (employee is null)
            return Result<MyProfileResponse>.NotFound("No employee record for the current user.");

        // Self access always passes visibility - reuse the existing label-resolution join rather
        // than re-deriving department/position/manager names.
        var visible = await _featureEmployees.GetVisibleByIdAsync(
            tenantId, EmployeeVisibilityScope.Unrestricted(), employee.Id, ct);

        var versionToken = await _featureEmployees.GetVersionTokenAsync(tenantId, employee.Id, ct);

        var addresses = await _profile.ListAddressesAsync(tenantId, employee.Id, ct);
        var emergencyContacts = await _profile.ListEmergencyContactsAsync(tenantId, employee.Id, ct);
        var dependents = await _profile.ListDependentsAsync(tenantId, employee.Id, ct);
        var bankDetail = await _profile.GetPrimaryBankDetailAsync(tenantId, employee.Id, ct);

        var workModes = await _workModes.ListActiveAsync(ct);
        var workModeLabel = workModes.FirstOrDefault(w => w.Id == employee.WorkModeId)?.Label ?? "Unknown";

        var personalInformation = new MyPersonalInformationResponse(
            employee.FirstName, employee.LastName, employee.Email, employee.Phone,
            employee.DateOfBirth, employee.Gender, employee.NationalityId, null,
            employee.DisplayTimezone, null,
            addresses.Select(a => new MyAddressResponse(a.Id, a.AddressType, a.AddressJson, a.IsPrimary)).ToList(),
            versionToken?.ToString() ?? string.Empty);

        var jobInformation = new MyJobInformationResponse(
            visible?.EmployeeNumber ?? employee.EmployeeNumber,
            visible?.LegalEntityName, visible?.DepartmentName, visible?.PositionName,
            visible?.ReportingManagerName, visible?.EmploymentTypeLabel ?? "Unknown",
            visible?.Status ?? "Unknown", employee.HireDate, employee.ProbationEndDate,
            workModeLabel);

        var maskedAccountNumber = bankDetail is null
            ? null
            : BankAccountMasker.Mask(_encryption.Decrypt(bankDetail.AccountNumberEncrypted));

        var payroll = new MyPayrollResponse(
            bankDetail is not null, bankDetail?.BankName, maskedAccountNumber, bankDetail?.AccountType,
            _currentUser.HasPermission("employees:write"));

        return Result<MyProfileResponse>.Success(new MyProfileResponse(
            personalInformation, jobInformation,
            emergencyContacts.Select(c => new MyEmergencyContactResponse(c.Id, c.Name, c.Relationship, c.Phone, c.Email, c.IsPrimary)).ToList(),
            dependents.Select(d => new MyDependentResponse(d.Id, d.Name, d.Relationship, d.DateOfBirth, d.IsEmergencyContact, d.Phone)).ToList(),
            payroll,
            new MySecurityResponse(false, null) /* wired to real MFA/password state in Task 9/10 */));
    }
}
