using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Type.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Type.Commands.UpdateLeaveType;

public record UpdateLeaveTypeCommand(
    Guid LeaveTypeId,
    string Name,
    string? Description,
    string Category,
    bool IsPaid,
    bool RequiresApproval,
    bool RequiresDocument,
    int? DocumentRequiredAfterDays,
    string[] AcceptedDocumentTypes,
    int? MaxConsecutiveDays,
    decimal DefaultDaysPerYear,
    bool CarryForwardAllowed,
    decimal? MaxCarryForwardDays,
    int? CarryForwardExpiryMonths,
    bool ProRataForNewJoiners,
    string ApplicableGender,
    int MinimumNoticeDays) : IRequest<Result<LeaveTypeResponse>>;
