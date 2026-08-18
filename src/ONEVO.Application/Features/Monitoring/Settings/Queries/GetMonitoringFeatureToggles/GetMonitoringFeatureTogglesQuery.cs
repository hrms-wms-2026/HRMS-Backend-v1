using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Settings.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Settings.Queries.GetMonitoringFeatureToggles;

public record GetMonitoringFeatureTogglesQuery : IRequest<Result<MonitoringFeatureTogglesResponse>>;
