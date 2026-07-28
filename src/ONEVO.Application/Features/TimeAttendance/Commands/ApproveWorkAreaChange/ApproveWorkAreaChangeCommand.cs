using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.TimeAttendance.Commands.ApproveWorkAreaChange;

public sealed record ApproveWorkAreaChangeCommand(
    Guid RequestId,
    uint ExpectedVersion,
    string? ReviewComment,
    Guid ReviewerTenantId,
    Guid ReviewerUserId) : IRequest<Result>;

