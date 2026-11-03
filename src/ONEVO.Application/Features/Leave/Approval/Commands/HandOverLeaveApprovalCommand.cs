using FluentValidation;
using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.Leave.Approval.Commands;

/// <summary>The current approver hands their pending step to someone else (Inbox "Escalate").</summary>
public sealed record HandOverLeaveApprovalCommand(Guid RequestId, Guid TargetEmployeeId, string? Note) : IRequest<Result>;

public sealed class HandOverLeaveApprovalCommandValidator : AbstractValidator<HandOverLeaveApprovalCommand>
{
    public HandOverLeaveApprovalCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.TargetEmployeeId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(2000);
    }
}

public sealed class HandOverLeaveApprovalCommandHandler : IRequestHandler<HandOverLeaveApprovalCommand, Result>
{
    private readonly LeaveApprovalDecisionService _service;
    public HandOverLeaveApprovalCommandHandler(LeaveApprovalDecisionService service) => _service = service;
    public Task<Result> Handle(HandOverLeaveApprovalCommand request, CancellationToken ct) =>
        _service.HandOverAsync(request.RequestId, request.TargetEmployeeId, request.Note, ct);
}
