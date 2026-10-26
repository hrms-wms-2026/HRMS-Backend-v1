using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Type.DTOs.Responses;

namespace ONEVO.Application.Features.Leave.Type.Commands.UpdateLeaveType;

public record UpdateLeaveTypeCommand(
    Guid LeaveTypeId,
    string Name,
    string? Description,
    string Color,
    bool IsPaid,
    string ApplicableGender,
    bool RequiresDocument,
    decimal? DocumentRequiredAfterHours,
    string[] AcceptedDocumentTypes) : IRequest<Result<LeaveTypeResponse>>;
