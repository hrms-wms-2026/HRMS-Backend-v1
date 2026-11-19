using FluentValidation;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Approval.DTOs.Responses;
using ONEVO.Application.Features.Leave.Request.Helpers;
using ONEVO.Application.Features.Leave.Request.RepositoryInterfaces;
using ONEVO.Domain.Features.Leave.Request.Entities;

namespace ONEVO.Application.Features.Leave.Approval.Commands;

public sealed record ListMyLeaveApprovalDelegatesQuery
    : IRequest<Result<IReadOnlyList<LeaveApprovalDelegateResponse>>>;

public sealed class ListMyLeaveApprovalDelegatesQueryHandler
    : IRequestHandler<ListMyLeaveApprovalDelegatesQuery, Result<IReadOnlyList<LeaveApprovalDelegateResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IEmployeeRepository _employees;
    private readonly ILeaveRequestRepository _requests;

    public ListMyLeaveApprovalDelegatesQueryHandler(
        ICurrentUser currentUser, IEmployeeRepository employees, ILeaveRequestRepository requests)
    {
        _currentUser = currentUser;
        _employees = employees;
        _requests = requests;
    }

    public async Task<Result<IReadOnlyList<LeaveApprovalDelegateResponse>>> Handle(
        ListMyLeaveApprovalDelegatesQuery query, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<LeaveApprovalDelegateResponse>>.Forbidden();
        if (_currentUser.TenantId == Guid.Empty)
            return Result<IReadOnlyList<LeaveApprovalDelegateResponse>>.Forbidden("Tenant context missing.");

        var employee = await _employees.GetByUserIdAsync(_currentUser.TenantId, _currentUser.UserId, ct);
        if (employee is null)
            return Result<IReadOnlyList<LeaveApprovalDelegateResponse>>.NotFound(LeaveRequestMessages.NoEmployeeRecord);

        var rows = await _requests.ListDelegatesForApproverAsync(_currentUser.TenantId, employee.Id, ct);
        return Result<IReadOnlyList<LeaveApprovalDelegateResponse>>.Success(
            rows.Select(row => new LeaveApprovalDelegateResponse(
                row.Id, row.DelegateEmployeeId, row.DelegateName, row.StartDate, row.EndDate)).ToList());
    }
}

public sealed record CreateLeaveApprovalDelegateCommand(
    Guid DelegateEmployeeId,
    DateOnly StartDate,
    DateOnly EndDate) : IRequest<Result<LeaveApprovalDelegateResponse>>;

public sealed class CreateLeaveApprovalDelegateCommandValidator : AbstractValidator<CreateLeaveApprovalDelegateCommand>
{
    public CreateLeaveApprovalDelegateCommandValidator()
    {
        RuleFor(x => x.DelegateEmployeeId).NotEmpty();
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("Cover end date must be on or after the start date.");
    }
}

public sealed class CreateLeaveApprovalDelegateCommandHandler
    : IRequestHandler<CreateLeaveApprovalDelegateCommand, Result<LeaveApprovalDelegateResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IEmployeeRepository _employees;
    private readonly ILeaveRequestRepository _requests;
    private readonly IUnitOfWork _unitOfWork;

    public CreateLeaveApprovalDelegateCommandHandler(
        ICurrentUser currentUser,
        IEmployeeRepository employees,
        ILeaveRequestRepository requests,
        IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _employees = employees;
        _requests = requests;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<LeaveApprovalDelegateResponse>> Handle(
        CreateLeaveApprovalDelegateCommand command, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeaveApprovalDelegateResponse>.Forbidden();
        if (_currentUser.TenantId == Guid.Empty)
            return Result<LeaveApprovalDelegateResponse>.Forbidden("Tenant context missing.");

        var approver = await _employees.GetByUserIdAsync(_currentUser.TenantId, _currentUser.UserId, ct);
        if (approver is null)
            return Result<LeaveApprovalDelegateResponse>.NotFound(LeaveRequestMessages.NoEmployeeRecord);
        if (command.DelegateEmployeeId == approver.Id)
            return Result<LeaveApprovalDelegateResponse>.Failure("You cannot cover your own approvals.");

        var delegateEmployee = await _employees.GetByIdAsync(_currentUser.TenantId, command.DelegateEmployeeId, ct);
        if (delegateEmployee is null)
            return Result<LeaveApprovalDelegateResponse>.NotFound("That employee was not found.");

        var existing = await _requests.ListDelegatesForApproverAsync(_currentUser.TenantId, approver.Id, ct);
        if (existing.Any(row => row.StartDate <= command.EndDate && row.EndDate >= command.StartDate))
            return Result<LeaveApprovalDelegateResponse>.Conflict("That date range already has cover.");

        var entity = new LeaveApprovalDelegate
        {
            Id = Guid.NewGuid(),
            TenantId = _currentUser.TenantId,
            ApproverEmployeeId = approver.Id,
            DelegateEmployeeId = command.DelegateEmployeeId,
            StartDate = command.StartDate,
            EndDate = command.EndDate
        };
        await _requests.AddDelegateAsync(entity, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<LeaveApprovalDelegateResponse>.Success(new LeaveApprovalDelegateResponse(
            entity.Id,
            entity.DelegateEmployeeId,
            $"{delegateEmployee.FirstName} {delegateEmployee.LastName}".Trim(),
            entity.StartDate,
            entity.EndDate));
    }
}

public sealed record DeleteLeaveApprovalDelegateCommand(Guid Id) : IRequest<Result>;

public sealed class DeleteLeaveApprovalDelegateCommandHandler
    : IRequestHandler<DeleteLeaveApprovalDelegateCommand, Result>
{
    private readonly ICurrentUser _currentUser;
    private readonly IEmployeeRepository _employees;
    private readonly ILeaveRequestRepository _requests;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteLeaveApprovalDelegateCommandHandler(
        ICurrentUser currentUser,
        IEmployeeRepository employees,
        ILeaveRequestRepository requests,
        IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _employees = employees;
        _requests = requests;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteLeaveApprovalDelegateCommand command, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Forbidden();
        if (_currentUser.TenantId == Guid.Empty)
            return Result.Forbidden("Tenant context missing.");

        var approver = await _employees.GetByUserIdAsync(_currentUser.TenantId, _currentUser.UserId, ct);
        if (approver is null)
            return Result.NotFound(LeaveRequestMessages.NoEmployeeRecord);

        var entity = await _requests.GetTrackedDelegateAsync(_currentUser.TenantId, command.Id, ct);
        if (entity is null)
            return Result.NotFound("That cover period was not found.");
        if (entity.ApproverEmployeeId != approver.Id)
            return Result.Forbidden();

        _requests.RemoveDelegate(entity);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
