using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Policy.DTOs;

namespace ONEVO.Application.Features.Monitoring.Policy.Queries.GetEffectiveTrayPolicy;

public sealed record GetEffectiveTrayPolicyQuery : IRequest<Result<TrayAgentPolicyDto>>;
