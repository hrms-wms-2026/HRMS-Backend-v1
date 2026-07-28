using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.TimeAttendance.Commands.ApproveRemoteLocationChange;

public sealed record ApproveRemoteLocationChangeCommand(
    Guid RequestId,
    uint ExpectedVersion,
    string? ReviewComment,
    Guid ReviewerTenantId,
    Guid ReviewerUserId) : IRequest<Result>;

