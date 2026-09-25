using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.RejectTaskStatusChangeRequest;

public sealed record RejectTaskStatusChangeRequestCommand(Guid RequestId, string? Comment) : IRequest<Result>;
