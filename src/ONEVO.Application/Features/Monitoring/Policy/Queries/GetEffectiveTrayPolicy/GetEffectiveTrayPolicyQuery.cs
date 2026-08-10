using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Policy.DTOs;

namespace ONEVO.Application.Features.Monitoring.Policy.Queries.GetEffectiveTrayPolicy;

/// <summary>
/// Fetches the effective monitoring policy for the authenticated tray device.
/// Carries no data: the employee/tenant identity comes exclusively from the
/// authenticated Device JWT via <see cref="ONEVO.Application.Features.Monitoring.CheckIn.ServiceInterfaces.ITrayCurrentDevice"/>.
/// </summary>
public sealed record GetEffectiveTrayPolicyQuery : IRequest<Result<TrayAgentPolicyDto>>;
