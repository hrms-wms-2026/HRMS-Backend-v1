using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Dashboard.Layout.Registry;

namespace ONEVO.Application.Features.Dashboard.Layout.Commands.AddDashboardWidget;

public sealed record AddWidgetResultDto(Guid InstanceId, long LayoutVersion);

public sealed record AddDashboardWidgetCommand(Guid DashboardId, string WidgetKey, long ExpectedVersion, GridRect? DesktopRect)
    : IRequest<Result<AddWidgetResultDto>>;
