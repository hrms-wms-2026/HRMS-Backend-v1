using FluentValidation;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Leave.Approval.DTOs.Responses;
using ONEVO.Application.Features.Leave.Approval.Helpers;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Approval.Services;

namespace ONEVO.Application.Features.Leave.Approval.Commands;

/// <summary>
/// The approver shortens or shifts a pending request and approves it in one step (plan L1, P3). The new range runs
/// the full submission rules; on any rule failure nothing changes (422 with the rule's message). The employee gets
/// the usual approval notification; the approver comment says what changed. Leave type is never editable.
/// </summary>
public sealed record ApproveEditedLeaveRequestCommand(Guid RequestId, DateTimeOffset StartAt, DateTimeOffset EndAt, string? Comment)
    : IRequest<Result<LeaveApprovalDecisionResponse>>;

public sealed class ApproveEditedLeaveRequestCommandValidator : AbstractValidator<ApproveEditedLeaveRequestCommand>
{
    public ApproveEditedLeaveRequestCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.EndAt).GreaterThan(x => x.StartAt);
        RuleFor(x => x.Comment).MaximumLength(1500);
    }
}

public sealed class ApproveEditedLeaveRequestCommandHandler
    : IRequestHandler<ApproveEditedLeaveRequestCommand, Result<LeaveApprovalDecisionResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IEmployeeRepository _employees;
    private readonly ILeaveApprovalRepository _repository;
    private readonly LeaveRequestRangeEditor _editor;
    private readonly LeaveApprovalDecisionService _decisions;
    private readonly IUnitOfWork _unitOfWork;

    public ApproveEditedLeaveRequestCommandHandler(
        ICurrentUser currentUser, IEmployeeRepository employees, ILeaveApprovalRepository repository, LeaveRequestRangeEditor editor,
        LeaveApprovalDecisionService decisions, IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _employees = employees;
        _repository = repository;
        _editor = editor;
        _decisions = decisions;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<LeaveApprovalDecisionResponse>> Handle(ApproveEditedLeaveRequestCommand command, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<LeaveApprovalDecisionResponse>.Forbidden(LeaveApprovalMessages.AuthRequired);
        var employee = await _employees.GetByUserIdAsync(_currentUser.TenantId, _currentUser.UserId, ct);
        if (employee is null)
            return Result<LeaveApprovalDecisionResponse>.NotFound(LeaveApprovalMessages.NoEmployee);
        var state = await _repository.GetStateAsync(_currentUser.TenantId, command.RequestId, ct);
        if (state is null)
            return Result<LeaveApprovalDecisionResponse>.NotFound(LeaveApprovalMessages.NotFound);
        if (!LeavePendingApprovals.IsActionable(state, employee.Id))
            return Result<LeaveApprovalDecisionResponse>.Forbidden(LeaveApprovalMessages.NotAssigned);

        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
            {
                var edited = await _editor.ApplyAsync(state, command.StartAt, command.EndAt, "Dates changed by the approver", innerCt);
                if (!edited.IsSuccess)
                    throw new EditAborted(Result<LeaveApprovalDecisionResponse>.Failure(edited.Error!, edited.StatusCode ?? 422));

                var (before, after) = edited.Value;
                var note = $"Approved with changes: {LeaveRequestRangeEditor.Describe(before)} -> {LeaveRequestRangeEditor.Describe(after)}";
                var comment = string.IsNullOrWhiteSpace(command.Comment) ? note : $"{note}. {command.Comment.Trim()}";
                var approved = await _decisions.ApproveAsync(command.RequestId, comment, innerCt);
                if (!approved.IsSuccess)
                    throw new EditAborted(approved);
                return approved;
            }, ct);
        }
        catch (EditAborted aborted)
        {
            // The transaction rolled back; drop the in-memory edits so nothing later flushes them.
            _unitOfWork.ClearTracking();
            return aborted.Result;
        }
    }

    private sealed class EditAborted(Result<LeaveApprovalDecisionResponse> result) : Exception(result.Error)
    {
        public Result<LeaveApprovalDecisionResponse> Result { get; } = result;
    }
}
