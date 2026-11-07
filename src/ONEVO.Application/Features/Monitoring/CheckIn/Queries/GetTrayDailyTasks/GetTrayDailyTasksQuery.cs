namespace ONEVO.Application.Features.Monitoring.CheckIn.Queries.GetTrayDailyTasks;

using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.CheckIn.DTOs;

public sealed record GetTrayDailyTasksQuery : IRequest<Result<TrayDailyTasksDto>>;
