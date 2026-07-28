using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.TimeAttendance.Commands.RejectWorkAreaChange;

public sealed record RejectWorkAreaChangeCommand(
    Guid RequestId,
    uint ExpectedVersion,
    string? ReviewComment,
    Guid ReviewerTenantId,
    Guid ReviewerUserId) : IRequest<Result>;

