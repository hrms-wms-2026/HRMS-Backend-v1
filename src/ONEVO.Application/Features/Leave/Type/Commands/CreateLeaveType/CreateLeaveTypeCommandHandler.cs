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
            Color = request.Color.Trim().ToUpperInvariant(),
            IsPaid = request.IsPaid,
            ApplicableGender = request.ApplicableGender,
            RequiresDocument = request.RequiresDocument,
            DocumentRequiredAfterHours = request.RequiresDocument ? request.DocumentRequiredAfterHours : null,
            AcceptedDocumentTypes = request.AcceptedDocumentTypes ?? [],
            IsActive = true,
            CreatedAt = _dateTimeProvider.UtcNow
        };

        await _leaveTypes.AddAsync(entity, ct);
        await _leaveTypes.SaveChangesAsync(ct);

        return Result<LeaveTypeResponse>.Success(LeaveTypeMapper.ToResponse(entity));
    }
}
