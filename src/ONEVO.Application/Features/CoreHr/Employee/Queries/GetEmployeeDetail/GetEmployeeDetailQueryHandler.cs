using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Helpers;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployeeDetail;

/// <summary>
/// Admin-facing full detail read for one employee - Job/Personal Info and Emergency Contacts are
/// always included once the caller passes the same employees:read + coverage check
/// GetEmployeeQueryHandler already enforces; Payroll is included only when the caller additionally
/// holds employees:read:sensitive (omitted, not a separate 403, so the rest of the screen still
/// renders for a caller without it).
/// </summary>
public class GetEmployeeDetailQueryHandler : IRequestHandler<GetEmployeeDetailQuery, Result<EmployeeDetailResponse>>
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IEmployeeVisibilityScopeResolver _visibilityScopeResolver;
    private readonly IEmployeeProfileRepository _profile;
    private readonly IInvitationTokenRepository _invitationTokenRepository;
    private readonly IEncryptionService _encryption;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public GetEmployeeDetailQueryHandler(
        IEmployeeRepository employeeRepository,
        IEmployeeVisibilityScopeResolver visibilityScopeResolver,
        IEmployeeProfileRepository profile,
        IInvitationTokenRepository invitationTokenRepository,
        IEncryptionService encryption,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _employeeRepository = employeeRepository;
        _visibilityScopeResolver = visibilityScopeResolver;
        _profile = profile;
        _invitationTokenRepository = invitationTokenRepository;
        _encryption = encryption;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<EmployeeDetailResponse>> Handle(GetEmployeeDetailQuery request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;

        var existing = await _employeeRepository.GetByIdAsync(tenantId, request.EmployeeId, ct);
        if (existing is null)
            return Result<EmployeeDetailResponse>.NotFound("The employee or selected organization record could not be found.");

        var scope = _currentUser.HasPermission("org:manage")
            ? EmployeeVisibilityScope.Unrestricted()
            : await _visibilityScopeResolver.ResolveAsync(tenantId, _currentUser.UserId, ct);

        var visible = await _employeeRepository.GetVisibleByIdAsync(tenantId, scope, request.EmployeeId, ct);
        if (visible is null)
            return Result<EmployeeDetailResponse>.Forbidden("You do not have access to manage this employee.");

        var addresses = await _profile.ListAddressesAsync(tenantId, request.EmployeeId, ct);
        var emergencyContacts = await _profile.ListEmergencyContactsAsync(tenantId, request.EmployeeId, ct);

        EmployeeDetailPayroll? payroll = null;
        if (_currentUser.HasPermission("employees:read:sensitive"))
        {
            var bankDetail = await _profile.GetPrimaryBankDetailAsync(tenantId, request.EmployeeId, ct);
            var maskedAccountNumber = bankDetail is null
                ? null
                : BankAccountMasker.Mask(_encryption.Decrypt(bankDetail.AccountNumberEncrypted));
            payroll = new EmployeeDetailPayroll(bankDetail is not null, bankDetail?.BankName, maskedAccountNumber, bankDetail?.AccountType);
        }

        var invitation = await _invitationTokenRepository.GetLatestByEmployeeIdAsync(tenantId, request.EmployeeId, ct);

        var jobInformation = new EmployeeDetailJobInformation(
            visible.EmployeeNumber, existing.LegalEntityId, visible.LegalEntityName, visible.DepartmentName, visible.PositionName,
            visible.PositionId, visible.ReportingManagerName, visible.EmploymentTypeLabel, visible.Status,
            existing.HireDate, existing.ProbationEndDate);

        var personalInformation = new EmployeeDetailPersonalInformation(
            existing.FirstName, existing.LastName, existing.Email, existing.Phone, existing.DateOfBirth,
            existing.Gender, existing.NationalityId,
            addresses.Select(a => new EmployeeDetailAddress(a.Id, a.AddressType, a.AddressJson, a.IsPrimary)).ToList());

        return Result<EmployeeDetailResponse>.Success(new EmployeeDetailResponse(
            request.EmployeeId, jobInformation, personalInformation,
            emergencyContacts.Select(c => new EmployeeDetailEmergencyContact(c.Id, c.Name, c.Relationship, c.Phone, c.Email, c.IsPrimary)).ToList(),
            payroll,
            InvitationStatusOf(invitation, _clock.UtcNow), invitation?.ExpiresAt));
    }

    private static string? InvitationStatusOf(InvitationToken? invitation, DateTimeOffset now)
    {
        if (invitation is null) return null;
        if (invitation.UsedAt is not null) return "accepted";
        if (invitation.RevokedAt is not null) return "revoked";
        if (invitation.ExpiresAt <= now) return "expired";
        return "pending";
    }
}
