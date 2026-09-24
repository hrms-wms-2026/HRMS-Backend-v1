using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.RejectTaskCreationRequest;

public sealed record RejectTaskCreationRequestCommand(Guid RequestId, string Comment) : IRequest<Result>;
