using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Employee.Models;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;

namespace ONEVO.Application.Features.CoreHr.Employee.Queries.GetEmployee;

public class GetEmployeeQueryHandler : IRequestHandler<GetEmployeeQuery, Result<EmployeeListItemResponse>>
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IEmployeeVisibilityScopeResolver _visibilityScopeResolver;
    private readonly IInvitationTokenRepository _invitationTokenRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public GetEmployeeQueryHandler(
        IEmployeeRepository employeeRepository,
        IEmployeeVisibilityScopeResolver visibilityScopeResolver,
        IInvitationTokenRepository invitationTokenRepository,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _employeeRepository = employeeRepository;
        _visibilityScopeResolver = visibilityScopeResolver;
        _invitationTokenRepository = invitationTokenRepository;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<EmployeeListItemResponse>> Handle(GetEmployeeQuery request, CancellationToken ct)
    {
        var existing = await _employeeRepository.GetByIdAsync(_currentUser.TenantId, request.EmployeeId, ct);
        if (existing is null)
        {
            return Result<EmployeeListItemResponse>.NotFound(
                "The employee or selected organization record could not be found.");
        }

        var scope = _currentUser.HasPermission("org:manage")
            ? EmployeeVisibilityScope.Unrestricted()
            : await _visibilityScopeResolver.ResolveAsync(_currentUser.TenantId, _currentUser.UserId, ct);

        var visible = await _employeeRepository.GetVisibleByIdAsync(
            _currentUser.TenantId, scope, request.EmployeeId, ct);

        var invitation = await _invitationTokenRepository.GetLatestByEmployeeIdAsync(
            _currentUser.TenantId, request.EmployeeId, ct);

        if (visible is null)
        {
            // Same invite-visibility exception as ListEmployeesQueryHandler: a brand-new invitee
            // has no coverage yet, but the person who invited them still needs to open the
            // detail page to manage/resend/revoke the invitation until it's accepted.
            var invitedByMePending = invitation is not null
                && invitation.CreatedById == _currentUser.UserId
                && invitation.UsedAt is null
                && invitation.RevokedAt is null;

            if (!invitedByMePending)
            {
                return Result<EmployeeListItemResponse>.Forbidden(
                    "You do not have access to manage this employee.");
            }

            visible = await _employeeRepository.GetVisibleByIdAsync(
                _currentUser.TenantId, EmployeeVisibilityScope.Unrestricted(), request.EmployeeId, ct);
            if (visible is null)
            {
                return Result<EmployeeListItemResponse>.NotFound(
                    "The employee or selected organization record could not be found.");
            }
        }

        return Result<EmployeeListItemResponse>.Success(visible with
        {
            InvitationStatus = InvitationStatusOf(invitation, _clock.UtcNow),
            InvitationExpiresAt = invitation?.ExpiresAt
        });
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
