using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Dashboard.Layout.Commands.RemoveDashboardWidget;

public sealed record RemoveDashboardWidgetCommand(Guid DashboardId, Guid InstanceId, long ExpectedVersion)
    : IRequest<Result<long>>;
