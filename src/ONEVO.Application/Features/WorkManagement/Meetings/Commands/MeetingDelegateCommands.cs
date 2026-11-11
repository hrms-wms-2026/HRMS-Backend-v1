using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Meetings.Entities;
using ONEVO.Domain.Lookups;

namespace ONEVO.Application.Features.WorkManagement.Meetings.Commands;

public sealed record ListMyMeetingDelegatesQuery : IRequest<Result<IReadOnlyList<MeetingDelegateView>>>;

public sealed record AddMeetingDelegateCommand(Guid DelegateEmployeeId, DateTimeOffset? ExpiresAt, bool CanPlanTasks = false)
    : IRequest<Result<MeetingDelegateView>>;

/// <summary>The principal turns "Can also plan my tasks" on or off on one of their active grants.</summary>
public sealed record UpdateMeetingDelegateCommand(Guid Id, bool CanPlanTasks) : IRequest<Result<MeetingDelegateView>>;

public sealed record RevokeMeetingDelegateCommand(Guid Id) : IRequest<Result<bool>>;

public sealed record ListMyMeetingPrincipalsQuery : IRequest<Result<IReadOnlyList<MeetingPrincipalView>>>;

/// <summary>
/// Meeting delegates (spec "Who manages delegates"): every employee grants, lists and revokes only their own delegates.
/// There is no way to grant or revoke for someone else.
/// </summary>
public sealed class MeetingDelegateCommandHandlers :
    IRequestHandler<ListMyMeetingDelegatesQuery, Result<IReadOnlyList<MeetingDelegateView>>>,
    IRequestHandler<AddMeetingDelegateCommand, Result<MeetingDelegateView>>,
    IRequestHandler<RevokeMeetingDelegateCommand, Result<bool>>,
    IRequestHandler<UpdateMeetingDelegateCommand, Result<MeetingDelegateView>>,
    IRequestHandler<ListMyMeetingPrincipalsQuery, Result<IReadOnlyList<MeetingPrincipalView>>>
{
    private static readonly HashSet<int> ActiveStatuses = [EmploymentStatusIds.Active, EmploymentStatusIds.OnLeave];

    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IMeetingDelegateRepository _delegates;
    private readonly IEmployeeRepository _employees;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;
    private readonly IMeetingDelegateAudit _audit;

    public MeetingDelegateCommandHandlers(ICurrentUser currentUser, ICallerIdentityResolver identity, IMeetingDelegateRepository delegates,
        IEmployeeRepository employees, IUnitOfWork unitOfWork, IDateTimeProvider clock, IMeetingDelegateAudit audit)
    {
        _currentUser = currentUser;
        _identity = identity;
        _delegates = delegates;
        _employees = employees;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _audit = audit;
    }

    public async Task<Result<IReadOnlyList<MeetingDelegateView>>> Handle(ListMyMeetingDelegatesQuery query, CancellationToken ct)
    {
        if (await CallerAsync(ct) is not Guid caller)
            return Result<IReadOnlyList<MeetingDelegateView>>.Forbidden("No employee record for the current user.");
        var rows = await _delegates.ListUnrevokedForPrincipalAsync(_currentUser.TenantId, caller, ct);
        var names = await NamesAsync(rows.Select(r => r.DelegateEmployeeId), ct);
        var now = _clock.UtcNow;
        return Result<IReadOnlyList<MeetingDelegateView>>.Success(
            rows.Select(r => View(r, names.GetValueOrDefault(r.DelegateEmployeeId) ?? string.Empty, now)).ToList());
    }

    public async Task<Result<MeetingDelegateView>> Handle(AddMeetingDelegateCommand command, CancellationToken ct)
    {
        if (await CallerAsync(ct) is not Guid caller)
            return Result<MeetingDelegateView>.Forbidden("No employee record for the current user.");
        var tenantId = _currentUser.TenantId;
        var now = _clock.UtcNow;
        if (command.DelegateEmployeeId == caller)
            return Result<MeetingDelegateView>.Failure("You cannot be your own meeting delegate.", 400, MeetingErrorCodes.DelegateSelf);
        if (command.ExpiresAt is { } expiresAt && expiresAt <= now)
            return Result<MeetingDelegateView>.Failure("The end date must be in the future.", 400, MeetingErrorCodes.DelegateExpiryInvalid);
        var person = await _employees.GetByIdAsync(tenantId, command.DelegateEmployeeId, ct);
        if (person is null || !ActiveStatuses.Contains(person.EmploymentStatusId))
            return Result<MeetingDelegateView>.Failure("That employee was not found.", 404, MeetingErrorCodes.DelegateEmployeeNotFound);

        return await _unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var existing = await _delegates.GetUnrevokedTrackedForPairAsync(tenantId, caller, person.Id, innerCt);
            if (existing is not null)
            {
                if (existing.ExpiresAt is null || existing.ExpiresAt > now)
                    return Result<MeetingDelegateView>.Failure($"{Name(person)} is already your meeting delegate.", 409,
                        MeetingErrorCodes.DelegateExists);
                // An expired grant is replaced: revoke it first, so the "one unrevoked grant per pair" index holds.
                existing.RevokedAt = now;
                await _audit.RecordDelegationAsync(existing, MeetingAuditActions.DelegateRevoked, innerCt);
                await _unitOfWork.SaveChangesAsync(innerCt);
            }

            var row = new MeetingDelegate
            {
                Id = Guid.NewGuid(), TenantId = tenantId, PrincipalEmployeeId = caller, DelegateEmployeeId = person.Id,
                CreatedByUserId = _currentUser.UserId, CreatedAt = now, ExpiresAt = command.ExpiresAt,
                CanPlanTasks = command.CanPlanTasks,
            };
            _delegates.Add(row);
            await _audit.RecordDelegationAsync(row, MeetingAuditActions.DelegateGranted, innerCt);
            await _unitOfWork.SaveChangesAsync(innerCt);
            return Result<MeetingDelegateView>.Success(View(row, Name(person), now));
        }, ct);
    }

    public async Task<Result<bool>> Handle(RevokeMeetingDelegateCommand command, CancellationToken ct)
    {
        if (await CallerAsync(ct) is not Guid caller)
            return Result<bool>.Forbidden("No employee record for the current user.");
        var row = await _delegates.GetUnrevokedTrackedAsync(_currentUser.TenantId, command.Id, ct);
        if (row is null || row.PrincipalEmployeeId != caller)
            return Result<bool>.Failure("That delegate was not found.", 404, MeetingErrorCodes.DelegateNotFound);
        row.RevokedAt = _clock.UtcNow;
        await _audit.RecordDelegationAsync(row, MeetingAuditActions.DelegateRevoked, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<MeetingDelegateView>> Handle(UpdateMeetingDelegateCommand command, CancellationToken ct)
    {
        if (await CallerAsync(ct) is not Guid caller)
            return Result<MeetingDelegateView>.Forbidden("No employee record for the current user.");
        var now = _clock.UtcNow;
        var row = await _delegates.GetUnrevokedTrackedAsync(_currentUser.TenantId, command.Id, ct);
        // Only the principal, and only while the grant is active: an expired one is replaced by adding the person again.
        if (row is null || row.PrincipalEmployeeId != caller || (row.ExpiresAt is { } expires && expires <= now))
            return Result<MeetingDelegateView>.Failure("That delegate was not found.", 404, MeetingErrorCodes.DelegateNotFound);
        var old = row.CanPlanTasks;
        row.CanPlanTasks = command.CanPlanTasks;
        await _audit.RecordDelegationChangeAsync(row, old, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        var names = await NamesAsync([row.DelegateEmployeeId], ct);
        return Result<MeetingDelegateView>.Success(View(row, names.GetValueOrDefault(row.DelegateEmployeeId) ?? string.Empty, now));
    }

    public async Task<Result<IReadOnlyList<MeetingPrincipalView>>> Handle(ListMyMeetingPrincipalsQuery query, CancellationToken ct)
    {
        if (await CallerAsync(ct) is not Guid caller)
            return Result<IReadOnlyList<MeetingPrincipalView>>.Forbidden("No employee record for the current user.");
        var principals = await _delegates.ListActivePrincipalsAsync(_currentUser.TenantId, caller, _clock.UtcNow, ct);
        var names = await NamesAsync(principals.Select(p => p.PrincipalEmployeeId), ct);
        return Result<IReadOnlyList<MeetingPrincipalView>>.Success(principals
            .Select(p => new MeetingPrincipalView(p.PrincipalEmployeeId, names.GetValueOrDefault(p.PrincipalEmployeeId) ?? string.Empty,
                p.CanPlanTasks))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    private static MeetingDelegateView View(MeetingDelegate row, string name, DateTimeOffset now)
        => new(row.Id, row.DelegateEmployeeId, name, row.CreatedAt, row.ExpiresAt,
            row.ExpiresAt is { } expires && expires <= now ? MeetingDelegateStates.Expired : MeetingDelegateStates.Active, row.CanPlanTasks);

    private static string Name(Employee e) => $"{e.FirstName} {e.LastName}".Trim();

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToList();
        if (distinct.Count == 0)
            return [];
        var people = await _employees.ListByIdsAsync(_currentUser.TenantId, distinct, ct);
        return people.ToDictionary(p => p.Key, p => Name(p.Value));
    }

    private async Task<Guid?> CallerAsync(CancellationToken ct)
        => _currentUser.IsAuthenticated ? await _identity.ResolveCallerEmployeeIdAsync(_currentUser.TenantId, _currentUser.UserId, ct) : null;
}
