using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;
using ONEVO.Application.Common.Auditing;

namespace ONEVO.Application.Features.Auth.Invite.Commands.InviteTenantAdmin;

public sealed record InviteTenantAdminCommand(
    Guid TenantId,
    string Email,
    string FirstName,
    string LastName,
    Guid RoleId,
    IReadOnlyList<string>? CompletionMethods,
    bool? AllowGoogleEmailMismatch,
    IReadOnlyList<string>? AllowedEmailDomains) : IRequest<Result<TenantInvitationDto>>, IAuditedCommand<Result<TenantInvitationDto>>
{
    public AuditEntry DescribeAudit(Result<TenantInvitationDto>? response)
        => AuditEntry.For("tenant.admin_invited", "tenant_user", response?.Value?.UserId, TenantId, new { Email, FirstName, LastName, RoleId, CompletionMethods, AllowGoogleEmailMismatch, AllowedEmailDomains });
}
