using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Queries.ListPendingAccessGrantRequestsForMe;

public sealed record ListPendingAccessGrantRequestsForMeQuery
    : IRequest<Result<IReadOnlyList<PendingAccessGrantRequestResponse>>>;

public sealed record PendingAccessGrantRequestResponse(
    Guid Id,
    string ActionType,
    string? EmployeeName,
    string TargetPositionName,
    string? ChangeReason,
    string RequestedByName,
    DateTimeOffset RequestedAt);
