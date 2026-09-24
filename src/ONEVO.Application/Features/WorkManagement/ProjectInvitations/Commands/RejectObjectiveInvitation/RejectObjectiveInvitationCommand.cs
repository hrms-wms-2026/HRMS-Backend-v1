using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.ProjectInvitations.Commands.RejectObjectiveInvitation;

public sealed record RejectObjectiveInvitationCommand(Guid InvitationId) : IRequest<Result>;
