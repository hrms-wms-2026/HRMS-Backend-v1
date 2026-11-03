using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Commands.ConfirmEventMeetingCleanup;

/// <summary>"I've deleted it in Teams/Zoom": the only way out of unresolved (spec §3.2.2).</summary>
public sealed record ConfirmEventMeetingCleanupCommand(Guid EventId) : IRequest<Result>;
