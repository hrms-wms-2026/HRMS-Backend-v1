using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.RevokeTenantSession;

public sealed record RevokeTenantSessionCommand(Guid TenantId, Guid SessionId) : IRequest<Result>;
