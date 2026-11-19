using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Dashboard.Layout.DTOs;

namespace ONEVO.Application.Features.Dashboard.Layout.Queries.GetDashboardBootstrap;

public sealed record GetDashboardBootstrapQuery(Guid DashboardId, string Breakpoint)
    : IRequest<Result<DashboardBootstrapDto>>;
