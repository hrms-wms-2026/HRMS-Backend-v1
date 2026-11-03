using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Dashboard.Layout.Commands.SaveDashboardLayout;

public sealed record LayoutItemDto(Guid InstanceId, int X, int Y, int W, int H);

public sealed record SaveDashboardLayoutCommand(
    Guid DashboardId, string Breakpoint, long ExpectedVersion, IReadOnlyList<LayoutItemDto> Widgets)
    : IRequest<Result<long>>;
