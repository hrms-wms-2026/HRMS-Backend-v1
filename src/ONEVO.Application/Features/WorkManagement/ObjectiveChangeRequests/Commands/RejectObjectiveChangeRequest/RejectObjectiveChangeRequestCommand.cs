using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.RejectObjectiveChangeRequest;

public sealed record RejectObjectiveChangeRequestCommand(Guid RequestId) : IRequest<Result>;
