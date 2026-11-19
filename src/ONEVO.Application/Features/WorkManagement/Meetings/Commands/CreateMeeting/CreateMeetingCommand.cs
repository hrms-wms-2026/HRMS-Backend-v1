using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Commands.CreateMeeting;

/// <summary>POST /meetings: create directly (D4 owner path, Office) or file a meeting.create request (member path).</summary>
public sealed record CreateMeetingCommand(CreateMeetingRequest Request) : IRequest<Result<MeetingCreatedResponse>>;
