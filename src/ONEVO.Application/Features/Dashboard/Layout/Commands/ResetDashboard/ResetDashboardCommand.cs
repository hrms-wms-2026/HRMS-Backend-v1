using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Dashboard.Layout.Commands.ResetDashboard;

public sealed record ResetDashboardCommand(Guid DashboardId, long ExpectedVersion) : IRequest<Result<long>>;
