using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.TimeAttendance.Commands.RejectRemoteLocationChange;

public sealed record RejectRemoteLocationChangeCommand(
    Guid RequestId,
    uint ExpectedVersion,
    string? ReviewComment,
    Guid ReviewerTenantId,
    Guid ReviewerUserId) : IRequest<Result>;

