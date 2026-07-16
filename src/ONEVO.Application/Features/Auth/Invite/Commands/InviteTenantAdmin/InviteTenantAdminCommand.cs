using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;

namespace ONEVO.Application.Features.Auth.Invite.Commands.InviteTenantAdmin;

public sealed record InviteTenantAdminCommand(
    Guid TenantId,
    string Email,
    string FirstName,
    string LastName,
    Guid RoleId,
    IReadOnlyList<string>? CompletionMethods,
    bool? AllowGoogleEmailMismatch,
    IReadOnlyList<string>? AllowedEmailDomains) : IRequest<Result<TenantInvitationDto>>;
