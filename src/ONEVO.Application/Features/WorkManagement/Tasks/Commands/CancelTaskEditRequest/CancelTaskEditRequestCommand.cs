using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.CancelTaskEditRequest;

public sealed record CancelTaskEditRequestCommand(Guid RequestId) : IRequest<Result>;
