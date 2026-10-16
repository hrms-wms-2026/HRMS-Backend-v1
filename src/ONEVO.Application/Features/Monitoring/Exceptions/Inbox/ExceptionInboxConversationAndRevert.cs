using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Escalation;
using ONEVO.Application.Features.Inbox.Revert;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.Inbox.Entities;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Inbox;

/// <summary>
/// The chat of an exception case (an investigation): the employee it is about (they post their justification) and the
/// routed reviewer from the case type's policy. Anyone whose exception scope covers the employee joins as a viewer and
/// may take the export. Scope is the caller's own (IExceptionScopeResolver reads the session), so other people are
/// never judged from here.
/// </summary>
public sealed class ExceptionConversationSubjectResolver : IConversationSubjectResolver
{
    private readonly IExceptionRepository _exceptions;
    private readonly IExceptionScopeResolver _scope;
    private readonly IInboxEscalationPolicyRegistry _policies;
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;

    public ExceptionConversationSubjectResolver(
        IExceptionRepository exceptions, IExceptionScopeResolver scope, IInboxEscalationPolicyRegistry policies,
        ICurrentUser currentUser, ICallerIdentityResolver identity)
    {
        _exceptions = exceptions;
        _scope = scope;
        _policies = policies;
        _currentUser = currentUser;
        _identity = identity;
    }

    public string Source => ExceptionInboxKinds.Source;

    public async Task<ConversationSubject?> ResolveAsync(Guid tenantId, ItemRef subject, CancellationToken ct)
    {
        if (subject.Kind != ExceptionInboxKinds.Case)
            return null;
        var exception = await _exceptions.GetByIdAsync(tenantId, subject.Id, ct);
        if (exception is null)
            return null;
        var people = new List<Guid> { exception.EmployeeId };
        if (_policies.For(subject.Source, exception.Type.ToString()) is { } policy
            && await policy.InitialAsync(tenantId, new EscalationContext(subject, exception.Type.ToString(), [], exception.EmployeeId, null, null), ct)
                is EscalationTarget.Employees reviewers)
            people.AddRange(reviewers.Recipients.Where(r => r.EmployeeId is not null).Select(r => r.EmployeeId!.Value));
        return new ConversationSubject(people.Distinct().ToList(), exception.Title, null, SubjectEmployeeId: exception.EmployeeId);
    }

    public Task<bool> CanViewAsync(Guid tenantId, ItemRef subject, Guid employeeId, CancellationToken ct)
        => InScopeAsync(tenantId, subject, employeeId, forAction: false, ct);

    /// <summary>The investigation export holds deleted and edited messages: it needs the scope that may work the case.</summary>
    public Task<bool> CanReviewAsync(Guid tenantId, ItemRef subject, Guid employeeId, CancellationToken ct)
        => InScopeAsync(tenantId, subject, employeeId, forAction: true, ct);

    private async Task<bool> InScopeAsync(Guid tenantId, ItemRef subject, Guid employeeId, bool forAction, CancellationToken ct)
    {
        if (await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct) != employeeId)
            return false;
        var exception = await _exceptions.GetByIdAsync(tenantId, subject.Id, ct);
        if (exception is null)
            return false;
        var scope = await _scope.ResolveAsync(forAction, [exception.EmployeeId], ct);
        return scope?.CanSee(exception.EmployeeId) == true;
    }
}

/// <summary>
/// The 30-minute undo of an acknowledge / resolve / escalate on an exception case: the case's previous review state
/// (status, timestamps, outcome, note) comes back. The reviewer must still have the case in their exception scope.
/// </summary>
public class ExceptionCaseReverter : InboxDecisionReverterBase
{
    private readonly IExceptionRepository _exceptions;
    private readonly IExceptionScopeResolver _scope;

    public ExceptionCaseReverter(
        IInboxDecisionJournal journal, IUnitOfWork unitOfWork, IDateTimeProvider clock, IExceptionRepository exceptions, IExceptionScopeResolver scope)
        : base(journal, unitOfWork, clock)
    {
        _exceptions = exceptions;
        _scope = scope;
    }

    public override string Source => ExceptionInboxKinds.Source;
    public override string Kind => ExceptionInboxKinds.Case;

    protected override async Task<Result> CheckDependenciesAsync(Guid tenantId, InboxDecisionJournalEntry decision, CancellationToken ct)
    {
        var exception = await _exceptions.GetByIdAsync(tenantId, decision.ItemId, ct);
        if (exception is null || ExceptionStateSnapshot.Parse(decision.BeforeJson) is null)
            return Result.NotFound("Exception not found.");
        // A change made outside the Inbox after this decision (e.g. on the Alerts page) is not journaled: never undo over it.
        if (ExceptionStateSnapshot.Parse(decision.AfterJson) is { } after && after.Status != exception.Status.ToString())
            return Result.Conflict("This case changed after your decision, so it can no longer be undone.");
        var scope = await _scope.ResolveAsync(forAction: true, [exception.EmployeeId], ct);
        return scope?.CanAct(exception.Id, exception.EmployeeId) == true ? Result.Success() : Result.Forbidden("You can no longer review this case.");
    }

    protected override async Task<Result<InboxActionResult>> ApplyRevertAsync(
        Guid tenantId, InboxDecisionJournalEntry decision, Guid callerEmployeeId, CancellationToken ct)
    {
        var exception = (await _exceptions.GetByIdAsync(tenantId, decision.ItemId, ct))!;
        ExceptionStateSnapshot.Parse(decision.BeforeJson)!.ApplyTo(exception);
        _exceptions.Update(exception);
        await _exceptions.SaveChangesAsync(ct);
        return Result<InboxActionResult>.Success(new InboxActionResult([exception.EmployeeId, callerEmployeeId]));
    }
}
