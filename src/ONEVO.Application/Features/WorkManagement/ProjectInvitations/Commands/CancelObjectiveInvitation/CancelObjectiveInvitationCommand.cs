using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.WorkManagement.ProjectInvitations.Commands.CancelObjectiveInvitation;

public sealed record CancelObjectiveInvitationCommand(Guid InvitationId) : IRequest<Result>;
