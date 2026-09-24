using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CancelTaskCreationRequest;

public sealed record CancelTaskCreationRequestCommand(Guid RequestId) : IRequest<Result>;
