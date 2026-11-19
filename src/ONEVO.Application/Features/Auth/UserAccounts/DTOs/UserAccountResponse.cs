namespace ONEVO.Application.Features.Auth.UserAccounts.DTOs;

/// <summary>
/// A row on Settings &gt; Users. The Can* flags describe what the account's state allows; the
/// screen additionally requires users:manage before offering any of them.
/// </summary>
public sealed record UserAccountResponse(
    Guid UserId,
    Guid? EmployeeId,
    string FullName,
    string Email,
    /// <summary>active | suspended | invited | invite_expired | invite_revoked | offboarded | inactive</summary>
    string Status,
    IReadOnlyList<string> Roles,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? InvitationExpiresAt,
    DateTimeOffset? SuspendedAt,
    int ActiveSessionCount,
    int ActiveDeviceCount,
    bool IsSelf,
    bool CanSuspend,
    bool CanReactivate,
    bool CanSignOut,
    /// <summary>Has at least one web session or tray device that can be revoked on its own.</summary>
    bool CanManageSessions,
    bool CanResendInvitation,
    bool CanRevokeInvitation);
