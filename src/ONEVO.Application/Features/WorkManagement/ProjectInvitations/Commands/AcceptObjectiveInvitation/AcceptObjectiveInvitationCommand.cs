using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.ProjectInvitations.Commands.AcceptObjectiveInvitation;

public sealed record AcceptObjectiveInvitationCommand(Guid InvitationId) : IRequest<Result>;
