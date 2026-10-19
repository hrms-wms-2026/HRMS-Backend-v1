using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Onboarding.Services;

namespace ONEVO.Application.Features.CoreHr.Onboarding.Inbox;

/// <summary>The chat on an onboarding approval: the requester and everyone who can decide it.</summary>
public sealed class OnboardingConversationSubjectResolver : IConversationSubjectResolver
{
    private readonly IOnboardingInboxRepository _rows;
    private readonly OnboardingApprovers _approvers;

    public OnboardingConversationSubjectResolver(IOnboardingInboxRepository rows, OnboardingApprovers approvers)
    {
        _rows = rows;
        _approvers = approvers;
    }

    public string Source => OnboardingInboxKinds.Source;

    public async Task<ConversationSubject?> ResolveAsync(Guid tenantId, ItemRef subject, CancellationToken ct)
    {
        var row = await _rows.GetAsync(tenantId, subject.Kind, subject.Id, ct);
        if (row is null)
            return null;
        var approvers = subject.Kind == OnboardingInboxKinds.SeatIncrease
            ? await _approvers.SeatApproversAsync(tenantId, null, ct)
            : await _approvers.PositionApproversAsync(tenantId, row.RequestedByUserId, ct);
        var people = approvers.Where(a => a.EmployeeId is not null).Select(a => a.EmployeeId!.Value).ToList();
        if (row.RequesterEmployeeId is { } requester)
            people.Add(requester);
        var title = subject.Kind == OnboardingInboxKinds.SeatIncrease
            ? "Seat increase"
            : $"Position approval - {row.SubjectName ?? "New employee"}";
        return new ConversationSubject(people.Distinct().ToList(), title, null);
    }
}
