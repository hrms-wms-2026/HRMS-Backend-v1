using System.Text.Json;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Application.Features.WorkManagement.Meetings.OutboxHandlers;

/// <summary>
/// Provisions a series link generation after its claim committed (spec 5.7). At-least-once: a message for a generation that
/// was already settled (or superseded) does nothing, and a repeat after a crash runs in recovery mode - Teams createOrGet and
/// the Zoom correlation lookup adopt the meeting an earlier attempt may have created instead of making a second one.
/// </summary>
public sealed class MeetingLinkProvisionOutboxHandler : IOutboxMessageHandler
{
    /// <summary>A provisioning row older than this may have had a provider call already.</summary>
    public static readonly TimeSpan RecoveryAfter = TimeSpan.FromMinutes(1);

    private readonly SeriesMeetingLinkService _service;
    private readonly IMeetingLinkRepository _links;
    private readonly IDateTimeProvider _clock;

    public MeetingLinkProvisionOutboxHandler(SeriesMeetingLinkService service, IMeetingLinkRepository links, IDateTimeProvider clock)
    {
        _service = service;
        _links = links;
        _clock = clock;
    }

    public string Type => OutboxMessageTypes.MeetingLinkProvision;

    public async Task HandleAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<MeetingLinkProvisionPayload>(payloadJson)
            ?? throw new InvalidOperationException("meeting_link_provision payload is empty.");
        var link = await _links.GetAsync(payload.TenantId, payload.LinkId, ct);
        if (link is not { Status: CalendarEventMeetingStatuses.Provisioning } || link.LinkGeneration != payload.Generation)
            return;
        var recovery = link.ProvisioningStartedAt is { } started && started < _clock.UtcNow - RecoveryAfter;
        await _service.ProvisionAsync(payload.TenantId, payload.LinkId, payload.SeriesId, recovery, ct);
    }
}
