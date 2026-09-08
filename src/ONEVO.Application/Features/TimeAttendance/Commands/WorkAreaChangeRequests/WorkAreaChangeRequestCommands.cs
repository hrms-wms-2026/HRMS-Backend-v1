using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

namespace ONEVO.Application.Features.TimeAttendance.Commands.WorkAreaChangeRequests;

public sealed record PreviewWorkAreaChangeRequestCommand(
    DateOnly Date,
    string RequestedWorkArea,
    string Reason) : IRequest<Result<WorkAreaChangeRequestPreviewResponse>>;

public sealed record CreateWorkAreaChangeRequestCommand(
    DateOnly Date,
    string RequestedWorkArea,
    string Reason) : IRequest<Result<WorkAreaChangeRequestResponse>>;

public sealed record ApproveWorkAreaChangeRequestCommand(Guid Id, string? ReviewComment)
    : IRequest<Result<WorkAreaChangeRequestResponse>>;

public sealed record RejectWorkAreaChangeRequestCommand(Guid Id, string ReviewComment)
    : IRequest<Result<WorkAreaChangeRequestResponse>>;

public sealed record CancelWorkAreaChangeRequestCommand(Guid Id)
    : IRequest<Result<WorkAreaChangeRequestResponse>>;
