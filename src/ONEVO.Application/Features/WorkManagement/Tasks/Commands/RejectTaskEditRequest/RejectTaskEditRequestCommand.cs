using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.RejectTaskEditRequest;

public sealed record RejectTaskEditRequestCommand(Guid RequestId, string Comment) : IRequest<Result>;
