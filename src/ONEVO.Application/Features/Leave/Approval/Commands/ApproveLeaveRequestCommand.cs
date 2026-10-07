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

public sealed record RequestLeaveInformationCommand(Guid RequestId, string Question)
    : IRequest<Result<LeaveApprovalDecisionResponse>>;

public sealed class RequestLeaveInformationCommandValidator : AbstractValidator<RequestLeaveInformationCommand>
{
    public RequestLeaveInformationCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.Question).NotEmpty().MaximumLength(2000);
    }
}

public sealed class RequestLeaveInformationCommandHandler
    : IRequestHandler<RequestLeaveInformationCommand, Result<LeaveApprovalDecisionResponse>>
{
    private readonly LeaveApprovalDecisionService _service;
    public RequestLeaveInformationCommandHandler(LeaveApprovalDecisionService service) => _service = service;
    public Task<Result<LeaveApprovalDecisionResponse>> Handle(RequestLeaveInformationCommand request, CancellationToken ct) =>
        _service.RequestInfoAsync(request.RequestId, request.Question, ct);
}

public sealed record ForwardLeaveRequestCommand(Guid RequestId, string? Note)
    : IRequest<Result<LeaveApprovalDecisionResponse>>;

public sealed class ForwardLeaveRequestCommandValidator : AbstractValidator<ForwardLeaveRequestCommand>
{
    public ForwardLeaveRequestCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(2000);
    }
}

public sealed class ForwardLeaveRequestCommandHandler
    : IRequestHandler<ForwardLeaveRequestCommand, Result<LeaveApprovalDecisionResponse>>
{
    private readonly LeaveApprovalDecisionService _service;
    public ForwardLeaveRequestCommandHandler(LeaveApprovalDecisionService service) => _service = service;
    public Task<Result<LeaveApprovalDecisionResponse>> Handle(ForwardLeaveRequestCommand request, CancellationToken ct) =>
        _service.ForwardAsync(request.RequestId, request.Note, ct);
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

public sealed record RespondLeaveInformationCommand(Guid RequestId, string Message, IReadOnlyList<Guid> FileRecordIds)
    : IRequest<Result<LeaveApprovalDecisionResponse>>;

public sealed class RespondLeaveInformationCommandValidator : AbstractValidator<RespondLeaveInformationCommand>
{
    public RespondLeaveInformationCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.Message).NotEmpty().MaximumLength(2000);
    }
}

public sealed class RespondLeaveInformationCommandHandler
    : IRequestHandler<RespondLeaveInformationCommand, Result<LeaveApprovalDecisionResponse>>
{
    private readonly LeaveApprovalDecisionService _service;
    public RespondLeaveInformationCommandHandler(LeaveApprovalDecisionService service) => _service = service;
    public Task<Result<LeaveApprovalDecisionResponse>> Handle(RespondLeaveInformationCommand request, CancellationToken ct) =>
        _service.RespondInfoAsync(request.RequestId, request.Message, request.FileRecordIds, ct);
}

public sealed record BulkApproveLeaveRequestsCommand(IReadOnlyList<Guid> RequestIds, string? Comment)
    : IRequest<Result<LeaveApprovalBulkResultResponse>>;

public sealed class BulkApproveLeaveRequestsCommandValidator : AbstractValidator<BulkApproveLeaveRequestsCommand>
{
    public BulkApproveLeaveRequestsCommandValidator()
    {
        RuleFor(x => x.RequestIds).NotEmpty();
        RuleFor(x => x.Comment).MaximumLength(2000);
    }
}

public sealed class BulkApproveLeaveRequestsCommandHandler
    : IRequestHandler<BulkApproveLeaveRequestsCommand, Result<LeaveApprovalBulkResultResponse>>
{
    private readonly IMediator _mediator;
    public BulkApproveLeaveRequestsCommandHandler(IMediator mediator) => _mediator = mediator;

    public async Task<Result<LeaveApprovalBulkResultResponse>> Handle(BulkApproveLeaveRequestsCommand command, CancellationToken ct)
    {
        var items = new List<LeaveApprovalBulkItemResponse>();
        foreach (var requestId in command.RequestIds.Distinct())
        {
            var result = await _mediator.Send(new ApproveLeaveRequestCommand(requestId, command.Comment), ct);
            items.Add(result.IsSuccess
                ? new LeaveApprovalBulkItemResponse(requestId, true, result.Value!.Status, null)
                : new LeaveApprovalBulkItemResponse(requestId, false, null, result.Error));
        }

        return Result<LeaveApprovalBulkResultResponse>.Success(new LeaveApprovalBulkResultResponse(
            items, items.Count(x => x.Success), items.Count(x => !x.Success)));
    }
}

public sealed record BulkRejectLeaveRequestsCommand(IReadOnlyList<Guid> RequestIds, string Reason)
    : IRequest<Result<LeaveApprovalBulkResultResponse>>;

public sealed class BulkRejectLeaveRequestsCommandValidator : AbstractValidator<BulkRejectLeaveRequestsCommand>
{
    public BulkRejectLeaveRequestsCommandValidator()
    {
        RuleFor(x => x.RequestIds).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
    }
}

public sealed class BulkRejectLeaveRequestsCommandHandler
    : IRequestHandler<BulkRejectLeaveRequestsCommand, Result<LeaveApprovalBulkResultResponse>>
{
    private readonly IMediator _mediator;
    public BulkRejectLeaveRequestsCommandHandler(IMediator mediator) => _mediator = mediator;

    public async Task<Result<LeaveApprovalBulkResultResponse>> Handle(BulkRejectLeaveRequestsCommand command, CancellationToken ct)
    {
        var items = new List<LeaveApprovalBulkItemResponse>();
        foreach (var requestId in command.RequestIds.Distinct())
        {
            var result = await _mediator.Send(new RejectLeaveRequestCommand(requestId, command.Reason), ct);
            items.Add(result.IsSuccess
                ? new LeaveApprovalBulkItemResponse(requestId, true, result.Value!.Status, null)
                : new LeaveApprovalBulkItemResponse(requestId, false, null, result.Error));
        }

        return Result<LeaveApprovalBulkResultResponse>.Success(new LeaveApprovalBulkResultResponse(
            items, items.Count(x => x.Success), items.Count(x => !x.Success)));
    }
}
