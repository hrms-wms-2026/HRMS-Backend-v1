using ONEVO.Application.Features.Dashboard.Team.DTOs;

namespace ONEVO.Application.Features.Dashboard.Team.Abstractions;

/// <summary>One row of the Approvals &amp; Exceptions widget (My Team spec §8.2, §9.4): a
/// source key, its domain ("people" or "work"), a cheap existence gate, and a summary. Each
/// implementation must derive PendingCount/TopItems from the exact same IQueryable predicate its
/// existing list endpoint already uses, so the two can never drift (spec §9.4 "Count/list
/// parity"). GetTeamActionItemsQueryHandler discovers every registered implementation via DI and
/// runs each one in its own try/catch - a source's own GetSummaryAsync never needs to handle its
/// own failure.</summary>
public interface ITeamActionSource
{
    string Key { get; }

    string Domain { get; }

    /// <summary>A cheap existence-only check: is the caller gated into this source at all. A
    /// false result omits the source from the response entirely (spec: "omitted, not zero") -
    /// never returned as a zero-count row.</summary>
    Task<bool> IsGatedAsync(CancellationToken ct = default);

    /// <summary>Only called after IsGatedAsync returns true. legalEntityId is the caller's active
    /// legal entity; top bounds TopItems (oldest first).</summary>
    Task<ActionSourceSummary> GetSummaryAsync(Guid legalEntityId, int top, CancellationToken ct = default);
}
