using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;

namespace ONEVO.Application.Features.TimeAttendance.Queries.AttendanceCorrections;

public sealed record ListMyAttendanceCorrectionsQuery(
    DateOnly? From,
    DateOnly? To,
    string? Status,
    PagedRequest Paging) : IRequest<Result<PagedResult<AttendanceCorrectionResponse>>>;

public sealed record ListAttendanceCorrectionApprovalsQuery(
    DateOnly? From,
    DateOnly? To,
    string? Status) : IRequest<Result<IReadOnlyList<AttendanceCorrectionResponse>>>;
