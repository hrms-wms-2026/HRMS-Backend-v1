using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CancelTaskStatusChangeRequest;

public sealed record CancelTaskStatusChangeRequestCommand(Guid RequestId) : IRequest<Result>;
