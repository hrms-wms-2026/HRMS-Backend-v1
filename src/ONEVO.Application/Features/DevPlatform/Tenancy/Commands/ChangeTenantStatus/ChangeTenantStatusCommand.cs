using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.ChangeTenantStatus;

public sealed record ChangeTenantStatusCommand(
    Guid TenantId,
    string Action,
    string? Reason) : IRequest<Result>;
