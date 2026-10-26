using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Dashboard.Layout.DTOs;

namespace ONEVO.Application.Features.Dashboard.Layout.Queries.GetWidgetData;

/// <summary>Refreshes one widget instance's data (ownership + widget visibility checked).</summary>
public sealed record GetWidgetDataQuery(Guid InstanceId) : IRequest<Result<WidgetDataResponseDto>>;
