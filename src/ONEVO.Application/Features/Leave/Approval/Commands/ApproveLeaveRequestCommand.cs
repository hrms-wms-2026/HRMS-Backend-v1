using FluentValidation;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Approval.DTOs.Responses;
using ONEVO.Application.Features.Leave.Approval.Helpers;

namespace ONEVO.Application.Features.Leave.Approval.Commands;

public sealed record ApproveLeaveRequestCommand(Guid RequestId, string? Comment)
    : IRequest<Result<LeaveApprovalDecisionResponse>>;

public sealed class ApproveLeaveRequestCommandValidator : AbstractValidator<ApproveLeaveRequestCommand>
{
    public ApproveLeaveRequestCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.Comment).MaximumLength(2000);
    }
}

public sealed class ApproveLeaveRequestCommandHandler
    : IRequestHandler<ApproveLeaveRequestCommand, Result<LeaveApprovalDecisionResponse>>
{
    private readonly LeaveApprovalDecisionService _service;
    public ApproveLeaveRequestCommandHandler(LeaveApprovalDecisionService service) => _service = service;
    public Task<Result<LeaveApprovalDecisionResponse>> Handle(ApproveLeaveRequestCommand request, CancellationToken ct) =>
        _service.ApproveAsync(request.RequestId, request.Comment, ct);
}

public sealed record RejectLeaveRequestCommand(Guid RequestId, string Reason)
    : IRequest<Result<LeaveApprovalDecisionResponse>>;

public sealed class RejectLeaveRequestCommandValidator : AbstractValidator<RejectLeaveRequestCommand>
{
    public RejectLeaveRequestCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
    }
}

public sealed class RejectLeaveRequestCommandHandler
    : IRequestHandler<RejectLeaveRequestCommand, Result<LeaveApprovalDecisionResponse>>
{
    private readonly LeaveApprovalDecisionService _service;
    public RejectLeaveRequestCommandHandler(LeaveApprovalDecisionService service) => _service = service;
    public Task<Result<LeaveApprovalDecisionResponse>> Handle(RejectLeaveRequestCommand request, CancellationToken ct) =>
        _service.RejectAsync(request.RequestId, request.Reason, ct);
}

public sealed record ChangeLeaveDecisionCommand(Guid RequestId, string Decision, string? Comment)
    : IRequest<Result<LeaveApprovalDecisionResponse>>;

public sealed class ChangeLeaveDecisionCommandValidator : AbstractValidator<ChangeLeaveDecisionCommand>
{
    public ChangeLeaveDecisionCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.Decision)
            .Must(d => d is ChangeLeaveDecisionValues.Approve or ChangeLeaveDecisionValues.Reject)
            .WithMessage("Decision must be 'approve' or 'reject'.");
        RuleFor(x => x.Comment).MaximumLength(1900);
        RuleFor(x => x.Comment).NotEmpty()
            .When(x => x.Decision == ChangeLeaveDecisionValues.Reject)
            .WithMessage("A reason is required to change a decision to rejected.");
    }
}

public sealed class ChangeLeaveDecisionCommandHandler
    : IRequestHandler<ChangeLeaveDecisionCommand, Result<LeaveApprovalDecisionResponse>>
{
    private readonly LeaveApprovalDecisionService _service;
    public ChangeLeaveDecisionCommandHandler(LeaveApprovalDecisionService service) => _service = service;
    public Task<Result<LeaveApprovalDecisionResponse>> Handle(ChangeLeaveDecisionCommand request, CancellationToken ct) =>
        _service.ChangeDecisionAsync(request.RequestId, request.Decision, request.Comment, ct);
}
