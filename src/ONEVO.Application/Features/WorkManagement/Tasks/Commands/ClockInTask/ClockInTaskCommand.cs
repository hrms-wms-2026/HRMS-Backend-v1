using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.ClockInTask;

public sealed record ClockInTaskCommand(Guid TaskId) : IRequest<Result>;
