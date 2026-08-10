using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.Commands.ApproveObjectiveChangeRequest;

public sealed record ApproveObjectiveChangeRequestCommand(Guid RequestId) : IRequest<Result>;
