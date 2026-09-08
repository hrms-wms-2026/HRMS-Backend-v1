using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.Tasks.Commands.AddClockingSessionReason;

public sealed record AddClockingSessionReasonCommand(Guid SessionId, string Reason) : IRequest<Result>;
