using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Sprints.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Sprints.Services;

/// <summary>Maps the submitter's outcome to the command result shared by every Sprint write command.</summary>
public static class SprintOutcomes
{
    public static Result<SprintWriteOutcome> ToWriteOutcome(Result<SprintActionOutcome> outcome)
    {
        if (!outcome.IsSuccess)
            return Result<SprintWriteOutcome>.Failure(outcome.Error!, outcome.StatusCode ?? 400);

        var value = outcome.Value!;
        return Result<SprintWriteOutcome>.Success(value.Applied
            ? new SprintWriteOutcome(value.Sprint is null ? null : SprintResponse.From(value.Sprint, canManage: true), null)
            : new SprintWriteOutcome(null, value.ApprovalRequestId));
    }
}
