using System.Security.Cryptography;
using System.Text.Json;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Provisioning;
using ONEVO.Application.Features.DevPlatform.Provisioning.OutboxHandlers;
using ONEVO.Application.Features.DevPlatform.Tenancy.Provisioning;
using ONEVO.Application.Features.DevPlatform.Provisioning.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Provisioning.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Services.DevPlatform.Provisioning;

public class TenantOwnerInvitationService : ITenantOwnerInvitationService
{
    private const int InviteValidityHours = 72;
    private const string OwnerRoleName = "Owner";

    private readonly ITenantRepository _tenants;
    private readonly IInvitationTokenRepository _invitations;
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IUserRoleRepository _userRoles;
    private readonly IOutboxWriter _outbox;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public TenantOwnerInvitationService(
        ITenantRepository tenants,
        IInvitationTokenRepository invitations,
        IUserRepository users,
        IRoleRepository roles,
        IUserRoleRepository userRoles,
        IOutboxWriter outbox,
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _tenants = tenants;
        _invitations = invitations;
        _users = users;
        _roles = roles;
        _userRoles = userRoles;
        _outbox = outbox;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<TenantInvitationDto>> InviteOwnerAsync(
        Guid tenantId,
        TenantOwnerInviteRequest request,
        CancellationToken ct)
    {
        // Standalone path: tenant must already exist (we are not orchestrated by CreateTenant).
        var tenant = await _tenants.GetByIdAsync(tenantId, ct);
        if (tenant is null)
            return Result<TenantInvitationDto>.NotFound($"Tenant '{tenantId}' not found.");

        // Require the Owner system role to already exist (seeded during provisioning).
        // Creating a permissionless role inline would yield a broken authorization setup.
        var ownerRole = await _roles.GetByNameForTenantAsync(tenant.Id, OwnerRoleName, ct);
        if (ownerRole is null)
            return Result<TenantInvitationDto>.Failure(
                "Tenant does not have a system Owner role. Run tenant provisioning first.",
                409);

        var pendingResult = await CreateInviteRecordsAsync(tenant.Id, ownerRole.Id, request, ct);
        if (!pendingResult.IsSuccess)
            return Result<TenantInvitationDto>.Failure(
                pendingResult.Error ?? "Failed to create invite records.",
                pendingResult.StatusCode ?? 400);

        // Invite records and the email outbox message commit in ONE transaction.
        await QueueInviteEmailAsync(pendingResult.Value!, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<TenantInvitationDto>.Success(
            new TenantInvitationDto(
                UserId: pendingResult.Value!.UserId,
                InviteExpiresAt: pendingResult.Value.ExpiresAt));
    }

    public async Task<Result<PendingOwnerInvite>> CreateInviteRecordsAsync(
        Guid tenantId,
        Guid roleId,
        TenantOwnerInviteRequest request,
        CancellationToken ct = default)
    {
        // NOTE: deliberately no tenant existence check here. The caller (either the
        // standalone InviteOwnerAsync or CreateTenantCommandHandler) is responsible
        // for tenant existence. The handler may have just added an unsaved Tenant in
        // the DbContext that GetByIdAsync (FirstOrDefaultAsync) cannot see.

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var now = _clock.UtcNow;

        var existingActive = await _invitations.GetActiveByEmailAsync(tenantId, normalizedEmail, now, ct);
        if (existingActive is not null)
            return Result<PendingOwnerInvite>.Conflict(
                $"An active invite for '{normalizedEmail}' already exists for this tenant.");

        var existingUser = await _users.GetByTenantAndEmailAsync(tenantId, normalizedEmail, ct);
        if (existingUser is not null)
            return Result<PendingOwnerInvite>.Conflict(
                $"A user with email '{normalizedEmail}' already exists in this tenant.");

        var (plaintextToken, tokenHash) = GenerateInviteToken();
        var expiresAt = now.AddHours(InviteValidityHours);

        var firstName = request.FirstName.Trim();
        var lastName = request.LastName.Trim();
        var fullName = $"{firstName} {lastName}".Trim();

        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = normalizedEmail,
            FirstName = firstName,
            LastName = lastName,
            PasswordHash = string.Empty,
            IsActive = false,
            MustChangePassword = true,
            PasswordSetByAdmin = false,
            TemporaryPasswordExpiresAt = expiresAt
        };
        await _users.AddAsync(user, ct);

        var userRole = new UserRole
        {
            UserId = user.Id,
            RoleId = roleId,
            AssignedAt = now,
            AssignedBy = _currentUser.UserId
        };
        await _userRoles.AddAsync(userRole, ct);

        var invitation = new InvitationToken
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = user.Id,
            RoleId = roleId,
            InvitedEmail = normalizedEmail,
            InvitedFullName = fullName,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            CreatedAt = now,
            CreatedById = _currentUser.UserId,
            CompletionMethodsJson = request.CompletionMethods is { Count: > 0 }
                ? JsonSerializer.Serialize(request.CompletionMethods)
                : null,
            AllowGoogleEmailMismatch = request.AllowGoogleEmailMismatch,
            AllowedEmailDomainsJson = request.AllowedEmailDomains is { Count: > 0 }
                ? JsonSerializer.Serialize(request.AllowedEmailDomains)
                : null
        };
        await _invitations.AddAsync(invitation, ct);

        // Tenant name is best-effort: the caller may still be holding an unsaved Tenant
        // in the DbContext, in which case GetByIdAsync returns null. We pass an empty
        // tenant name and let the caller (CreateTenantCommandHandler) override it via
        // a richer overload if needed. The standalone InviteOwnerAsync path will resolve
        // it from the already-persisted tenant before calling TrySendInviteEmailAsync.
        var tenantName = string.Empty;
        var tenant = await _tenants.GetByIdAsync(tenantId, ct);
        if (tenant is not null)
            tenantName = tenant.Name;

        var pending = new PendingOwnerInvite(
            UserId: user.Id,
            Email: normalizedEmail,
            FullName: fullName,
            PlaintextToken: plaintextToken,
            ExpiresAt: expiresAt,
            TenantId: tenantId,
            TenantName: tenantName,
            FirstName: firstName);

        return Result<PendingOwnerInvite>.Success(pending);
    }

    public Task QueueInviteEmailAsync(
        PendingOwnerInvite pending,
        CancellationToken ct = default)
    {
        // The payload carries the one-time plaintext token, so the outbox writer
        // stores it encrypted. The outbox handler resolves the tenant name at send
        // time when it was not known pre-commit.
        return _outbox.EnqueueAsync(
            OutboxMessageTypes.TenantOwnerInviteEmail,
            new TenantOwnerInviteEmailPayload(
                TenantId: pending.TenantId,
                TenantName: pending.TenantName,
                Email: pending.Email,
                FirstName: pending.FirstName,
                InviteToken: pending.PlaintextToken,
                ExpiresAt: pending.ExpiresAt),
            tenantId: pending.TenantId,
            ct);
    }

    private static (string Plaintext, string Hash) GenerateInviteToken()
    {
        Span<byte> raw = stackalloc byte[32];
        RandomNumberGenerator.Fill(raw);
        var plaintext = Convert.ToBase64String(raw)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        var hashBytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plaintext));
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return (plaintext, hash);
    }
}
