using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.Security;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.OnboardingDraft.OutboxHandlers;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using IUnitOfWork = ONEVO.Application.Common.RepositoryInterfaces.IUnitOfWork;

namespace ONEVO.Application.Features.CoreHr.Employee.Commands.ResendEmployeeInvitation;

/// <summary>
/// HR-triggered resend from the employee detail screen. Only usable once the previous invitation
/// has expired without being accepted - a still-pending invite should keep working via its
/// original link, and an already-accepted or revoked one is a different situation the caller
/// should not paper over by silently minting a new token. Revokes the stale token and creates a
/// fresh one, mirroring the token-issuance block in ApproveAccessGrantRequestCommandHandler
/// (same 24h validity, same Purpose/Position/LegalEntity/Employee/OnboardingDraft linkage carried
/// forward from the token being replaced) rather than re-deriving role/position assignment, which
/// was already done once when the access grant was approved and must not be repeated here.
/// </summary>
public sealed class ResendEmployeeInvitationCommandHandler
    : IRequestHandler<ResendEmployeeInvitationCommand, Result<ResendEmployeeInvitationResponse>>
{
    private const int InvitationValidityHours = 24;

    private readonly IEmployeeRepository _employeeRepository;
    private readonly IInvitationTokenRepository _invitationTokenRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly IOutboxWriter _outboxWriter;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public ResendEmployeeInvitationCommandHandler(
        IEmployeeRepository employeeRepository,
        IInvitationTokenRepository invitationTokenRepository,
        ITenantRepository tenantRepository,
        IOutboxWriter outboxWriter,
        ISecureTokenGenerator tokenGenerator,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _employeeRepository = employeeRepository;
        _invitationTokenRepository = invitationTokenRepository;
        _tenantRepository = tenantRepository;
        _outboxWriter = outboxWriter;
        _tokenGenerator = tokenGenerator;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<ResendEmployeeInvitationResponse>> Handle(
        ResendEmployeeInvitationCommand request, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId;

        var employee = await _employeeRepository.GetByIdAsync(tenantId, request.EmployeeId, ct);
        if (employee is null)
            return Result<ResendEmployeeInvitationResponse>.NotFound("The employee could not be found.");

        var current = await _invitationTokenRepository.GetLatestByEmployeeIdAsync(tenantId, request.EmployeeId, ct);
        if (current is null)
            return Result<ResendEmployeeInvitationResponse>.Failure("No invitation has been sent to this employee.", 400);

        var now = _clock.UtcNow;
        if (current.UsedAt is not null)
            return Result<ResendEmployeeInvitationResponse>.Failure("This invitation has already been accepted.", 400);
        if (current.RevokedAt is not null)
            return Result<ResendEmployeeInvitationResponse>.Failure("This invitation has been revoked.", 400);
        if (current.ExpiresAt > now)
            return Result<ResendEmployeeInvitationResponse>.Failure("This invitation has not expired yet.", 400);

        current.RevokedAt = now;
        current.RevokedById = _currentUser.UserId;

        var rawToken = _tokenGenerator.GenerateUrlSafeOpaqueToken();
        var expiresAt = now.AddHours(InvitationValidityHours);
        var fullName = $"{employee.FirstName.Trim()} {employee.LastName.Trim()}".Trim();

        var invitation = new InvitationToken
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = current.UserId,
            RoleId = null,
            PositionId = current.PositionId,
            Purpose = InvitationToken.EmployeeOnboardingPurpose,
            LegalEntityId = current.LegalEntityId,
            EmployeeId = request.EmployeeId,
            OnboardingDraftId = current.OnboardingDraftId,
            InvitedEmail = employee.Email.Trim(),
            InvitedFullName = fullName,
            TokenHash = InvitationTokenHasher.Hash(rawToken),
            ExpiresAt = expiresAt,
            CreatedAt = now,
            CreatedById = _currentUser.UserId,
        };
        await _invitationTokenRepository.AddAsync(invitation, ct);

        var tenant = await _tenantRepository.GetByIdAsync(tenantId, ct);
        await _outboxWriter.EnqueueAsync(
            OutboxMessageTypes.EmployeeOnboardingInviteEmail,
            new EmployeeOnboardingInviteEmailPayload(
                tenantId, current.LegalEntityId ?? employee.LegalEntityId ?? Guid.Empty, request.EmployeeId, invitation.Id,
                employee.Email.Trim(), employee.FirstName.Trim(), employee.LastName.Trim(), rawToken, expiresAt,
                tenant?.Slug),
            tenantId,
            ct);

        await _unitOfWork.SaveChangesAsync(ct);

        return Result<ResendEmployeeInvitationResponse>.Success(new ResendEmployeeInvitationResponse(expiresAt));
    }
}
