using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Calendar.Queries.GetEventMeetingAttendance;

public sealed record MeetingAttendeeItem(string? Name, string? Email, DateTimeOffset JoinedAt, DateTimeOffset? LeftAt, int? DurationSeconds);

public sealed record GetEventMeetingAttendanceResult(bool Available, IReadOnlyList<MeetingAttendeeItem> Attendees);

public sealed record GetEventMeetingAttendanceQuery(Guid EventId) : IRequest<Result<GetEventMeetingAttendanceResult>>;
