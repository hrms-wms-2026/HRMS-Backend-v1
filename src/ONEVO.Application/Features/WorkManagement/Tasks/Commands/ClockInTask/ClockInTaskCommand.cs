using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ClockInTask;

public sealed record ClockInTaskCommand(Guid TaskId) : IRequest<Result<ClockInTaskResponse>>;
