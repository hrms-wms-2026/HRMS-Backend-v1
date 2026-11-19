using ONEVO.Application.Features.Auth.UserAccounts.DTOs;
using ONEVO.Application.Features.Auth.UserAccounts.RepositoryInterfaces;
using ONEVO.Domain.Lookups;

namespace ONEVO.Application.Features.Auth.UserAccounts;

/// <summary>
/// Derives an account's status. User.IsActive = false means three different things (invite not
/// accepted yet, offboarded, suspended), so the precedence is: offboarded, suspended, active,
/// then the state of the latest invitation.
/// </summary>
public static class UserAccountStatus
{
    public const string Active = "active";
    public const string Suspended = "suspended";
    public const string Invited = "invited";
    public const string InviteExpired = "invite_expired";
    public const string InviteRevoked = "invite_revoked";
    public const string Offboarded = "offboarded";
    public const string Inactive = "inactive";

    public static bool IsOffboarded(int? employmentStatusId) =>
        employmentStatusId is EmploymentStatusIds.Terminated or EmploymentStatusIds.Resigned;

    public static string Of(UserAccountRow row, DateTimeOffset now)
    {
        if (!row.IsActive && IsOffboarded(row.EmploymentStatusId))
            return Offboarded;
        if (row.SuspendedAt is not null)
            return Suspended;
        if (row.IsActive)
            return Active;
        if (row.InvitationExpiresAt is not null && row.InvitationUsedAt is null)
        {
            if (row.InvitationRevokedAt is not null)
                return InviteRevoked;
            return row.InvitationExpiresAt <= now ? InviteExpired : Invited;
        }
        return Inactive;
    }

    public static UserAccountResponse ToResponse(UserAccountRow row, Guid currentUserId, DateTimeOffset now)
    {
        var status = Of(row, now);
        var isSelf = row.UserId == currentUserId;
        var hasEmployee = row.EmployeeId is not null;
        var fullName = $"{row.FirstName.Trim()} {row.LastName.Trim()}".Trim();

        return new UserAccountResponse(
            row.UserId,
            row.EmployeeId,
            string.IsNullOrEmpty(fullName) ? row.Email : fullName,
            row.Email,
            status,
            row.RoleNames,
            row.LastLoginAt,
            row.InvitationUsedAt is null ? row.InvitationExpiresAt : null,
            row.SuspendedAt,
            row.ActiveSessionCount,
            row.ActiveDeviceCount,
            isSelf,
            CanSuspend: status == Active && !isSelf && !row.HoldsOwnerRole,
            CanReactivate: status == Suspended,
            CanSignOut: !isSelf && row.ActiveSessionCount + row.ActiveDeviceCount > 0,
            // Your own other browsers/devices can be revoked too - just not the current session.
            CanManageSessions: row.ActiveSessionCount + row.ActiveDeviceCount > (isSelf ? 1 : 0),
            // Resend mints a new token only once the current one has expired (see
            // ResendEmployeeInvitationCommandHandler); revoke works on any open invitation.
            CanResendInvitation: hasEmployee && status == InviteExpired,
            CanRevokeInvitation: hasEmployee && status is Invited or InviteExpired);
    }
}
