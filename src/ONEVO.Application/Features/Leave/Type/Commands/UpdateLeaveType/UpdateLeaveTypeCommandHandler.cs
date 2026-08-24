using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Type.DTOs.Responses;
using ONEVO.Application.Features.Leave.Type.Mappers;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Type.Commands.UpdateLeaveType;

public class UpdateLeaveTypeCommandHandler : IRequestHandler<UpdateLeaveTypeCommand, Result<LeaveTypeResponse>>
{
    private readonly ILeaveTypeRepository _leaveTypes;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpdateLeaveTypeCommandHandler(
        ILeaveTypeRepository leaveTypes, ICurrentUser currentUser, IDateTimeProvider dateTimeProvider)
    {
        _leaveTypes = leaveTypes;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<LeaveTypeResponse>> Handle(UpdateLeaveTypeCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeaveTypeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var entity = await _leaveTypes.GetByIdAsync(tenantId, request.LeaveTypeId, ct);
        if (entity is null)
            return Result<LeaveTypeResponse>.NotFound("Leave type not found.");

        var name = request.Name.Trim();
        if (await _leaveTypes.ExistsByNameAsync(tenantId, name, excludingLeaveTypeId: entity.Id, ct))
            return Result<LeaveTypeResponse>.Conflict("A leave type with this name already exists.");

        entity.Name = name;
        entity.Description = request.Description?.Trim();
        entity.Category = request.Category;
        entity.IsPaid = request.IsPaid;
        entity.RequiresApproval = request.RequiresApproval;
        entity.RequiresDocument = request.RequiresDocument;
        entity.DocumentRequiredAfterDays = request.DocumentRequiredAfterDays;
        entity.AcceptedDocumentTypes = request.AcceptedDocumentTypes ?? [];
        entity.MaxConsecutiveDays = request.MaxConsecutiveDays;
        entity.DefaultDaysPerYear = request.DefaultDaysPerYear;
        entity.CarryForwardAllowed = request.CarryForwardAllowed;
        entity.MaxCarryForwardDays = request.MaxCarryForwardDays;
        entity.CarryForwardExpiryMonths = request.CarryForwardExpiryMonths;
        entity.ProRataForNewJoiners = request.ProRataForNewJoiners;
        entity.ApplicableGender = request.ApplicableGender;
        entity.MinimumNoticeDays = request.MinimumNoticeDays;
        entity.UpdatedAt = _dateTimeProvider.UtcNow;

        _leaveTypes.Update(entity);
        await _leaveTypes.SaveChangesAsync(ct);

        return Result<LeaveTypeResponse>.Success(LeaveTypeMapper.ToResponse(entity));
    }
}
