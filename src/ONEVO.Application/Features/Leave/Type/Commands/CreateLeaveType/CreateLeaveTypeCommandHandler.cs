using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Type.DTOs.Responses;
using ONEVO.Application.Features.Leave.Type.Mappers;
using ONEVO.Application.Features.Leave.Type.RepositoryInterfaces;
using LeaveTypeEntity = ONEVO.Domain.Features.Leave.Type.Entities.LeaveType;

namespace ONEVO.Application.Features.Leave.Type.Commands.CreateLeaveType;

public class CreateLeaveTypeCommandHandler : IRequestHandler<CreateLeaveTypeCommand, Result<LeaveTypeResponse>>
{
    private readonly ILeaveTypeRepository _leaveTypes;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;

    public CreateLeaveTypeCommandHandler(
        ILeaveTypeRepository leaveTypes, ICurrentUser currentUser, IDateTimeProvider dateTimeProvider)
    {
        _leaveTypes = leaveTypes;
        _currentUser = currentUser;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<LeaveTypeResponse>> Handle(CreateLeaveTypeCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeaveTypeResponse>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return Result<LeaveTypeResponse>.Forbidden("Tenant context missing.");

        var name = request.Name.Trim();
        var code = request.Code.Trim().ToUpperInvariant();

        if (await _leaveTypes.ExistsByNameAsync(tenantId, name, excludingLeaveTypeId: null, ct))
            return Result<LeaveTypeResponse>.Conflict("A leave type with this name already exists.");

        if (await _leaveTypes.ExistsByCodeAsync(tenantId, code, excludingLeaveTypeId: null, ct))
            return Result<LeaveTypeResponse>.Conflict("A leave type with this code already exists.");

        var entity = new LeaveTypeEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Code = code,
            Description = request.Description?.Trim(),
            Category = request.Category,
            IsPaid = request.IsPaid,
            RequiresApproval = request.RequiresApproval,
            RequiresDocument = request.RequiresDocument,
            DocumentRequiredAfterDays = request.DocumentRequiredAfterDays,
            AcceptedDocumentTypes = request.AcceptedDocumentTypes ?? [],
            MaxConsecutiveDays = request.MaxConsecutiveDays,
            DefaultDaysPerYear = request.DefaultDaysPerYear,
            CarryForwardAllowed = request.CarryForwardAllowed,
            MaxCarryForwardDays = request.MaxCarryForwardDays,
            CarryForwardExpiryMonths = request.CarryForwardExpiryMonths,
            ProRataForNewJoiners = request.ProRataForNewJoiners,
            ApplicableGender = request.ApplicableGender,
            MinimumNoticeDays = request.MinimumNoticeDays,
            IsActive = true,
            CreatedAt = _dateTimeProvider.UtcNow
        };

        await _leaveTypes.AddAsync(entity, ct);
        await _leaveTypes.SaveChangesAsync(ct);

        return Result<LeaveTypeResponse>.Success(LeaveTypeMapper.ToResponse(entity));
    }
}
