using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.Services;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;
using ONEVO.Infrastructure.ExternalServices.Email;

namespace ONEVO.Infrastructure.Services.DevPlatform.Support;

/// <summary>Configuration section "Support".</summary>
public sealed class SupportAlertOptions
{
    public const string SectionName = "Support";

    /// <summary>Shared support inbox that gets alerts for unassigned tickets. Blank = no alert for unassigned tickets.</summary>
    public string AlertEmail { get; set; } = string.Empty;
}

public sealed class SupportNotifier : ISupportNotifier
{
    public const string ReplyEventKey = "support_ticket_replied";
    public const string StatusEventKey = "support_ticket_status_changed";
    public const string RelatedEntityType = "support_ticket";
    public const string SupportManagePermission = "support:manage";

    private const int ExcerptLength = 500;

    private static readonly IReadOnlyDictionary<string, string> StatusLabels = new Dictionary<string, string>
    {
        [SupportTicket.StatusOpen] = "open",
        [SupportTicket.StatusInProgress] = "in progress",
        [SupportTicket.StatusResolved] = "resolved",
        [SupportTicket.StatusClosed] = "closed",
    };

    private readonly INotificationDispatcher _dispatcher;
    private readonly IPermissionRepository _permissions;
    private readonly ISupportTicketRepository _tickets;
    private readonly IEmailService _email;
    private readonly IDateTimeProvider _clock;
    private readonly SupportAlertOptions _options;
    private readonly EmailOptions _emailOptions;
    private readonly ILogger<SupportNotifier> _logger;

    public SupportNotifier(
        INotificationDispatcher dispatcher,
        IPermissionRepository permissions,
        ISupportTicketRepository tickets,
        IEmailService email,
        IDateTimeProvider clock,
        IOptions<SupportAlertOptions> options,
        IOptions<EmailOptions> emailOptions,
        ILogger<SupportNotifier> logger)
    {
        _dispatcher = dispatcher;
        _permissions = permissions;
        _tickets = tickets;
        _email = email;
        _clock = clock;
        _options = options.Value;
        _emailOptions = emailOptions.Value;
        _logger = logger;
    }

    public Task QueueTenantReplyAsync(SupportTicket ticket, SupportTicketComment comment, CancellationToken ct)
    {
        if (comment.IsInternal)
        {
            return Task.CompletedTask;
        }

        return QueueTenantAsync(ticket, ReplyEventKey, new Dictionary<string, string>
        {
            ["subject"] = ticket.Subject,
            ["authorName"] = comment.AuthorName,
        }, ct);
    }

    public Task QueueTenantStatusChangeAsync(SupportTicket ticket, CancellationToken ct) =>
        QueueTenantAsync(ticket, StatusEventKey, new Dictionary<string, string>
        {
            ["subject"] = ticket.Subject,
            ["status"] = StatusLabels.GetValueOrDefault(ticket.Status, ticket.Status),
        }, ct);

    public async Task AlertSupportAsync(
        SupportTicket ticket, SupportAlertKind kind, string actorName, string text, CancellationToken ct)
    {
        try
        {
            var to = ticket.AssignedToPlatformUserId is { } assignee
                ? await _tickets.GetActivePlatformUserEmailAsync(assignee, ct)
                : null;
            to ??= string.IsNullOrWhiteSpace(_options.AlertEmail) ? null : _options.AlertEmail.Trim();
            if (to is null)
            {
                _logger.LogInformation(
                    "Support alert for ticket {TicketId} not sent: no assignee and Support:AlertEmail is not set.", ticket.Id);
                return;
            }

            var tenantName = await _tickets.GetTenantNameAsync(ticket.TenantId, ct) ?? "A tenant";
            var subject = kind == SupportAlertKind.TicketRaised
                ? $"New support ticket from {tenantName}: {ticket.Subject}"
                : $"{tenantName} replied: {ticket.Subject}";
            await _email.SendAsync(to, Truncate(subject, 200), BuildAlertHtml(ticket, kind, tenantName, actorName, text), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Support alert email for ticket {TicketId} failed.", ticket.Id);
        }
    }

    private async Task QueueTenantAsync(
        SupportTicket ticket, string eventKey, IReadOnlyDictionary<string, string> placeholders, CancellationToken ct)
    {
        try
        {
            foreach (var userId in await ResolveTenantRecipientsAsync(ticket, ct))
            {
                await _dispatcher.SendTemplatedAsync(
                    ticket.TenantId, userId, eventKey, placeholders, RelatedEntityType, ticket.Id, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Support notification {EventKey} for ticket {TicketId} failed.", eventKey, ticket.Id);
        }
    }

    /// <summary>
    /// Who on the tenant side hears about the ticket: the person who raised it and anyone who replied. A ticket that
    /// support opened has neither yet, so it goes to everyone who can manage support for that tenant.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ResolveTenantRecipientsAsync(SupportTicket ticket, CancellationToken ct)
    {
        var recipients = new HashSet<Guid>(await _tickets.ListTenantCommenterIdsAsync(ticket.Id, ct));
        if (ticket.CreatedByUserId is { } creator)
        {
            recipients.Add(creator);
        }

        if (recipients.Count == 0)
        {
            recipients.UnionWith(await _permissions.ListUserIdsWithPermissionCodeAsync(
                ticket.TenantId, SupportManagePermission, _clock.UtcNow, ct));
        }

        return recipients.ToList();
    }

    private string BuildAlertHtml(SupportTicket ticket, SupportAlertKind kind, string tenantName, string actorName, string text)
    {
        var heading = kind == SupportAlertKind.TicketRaised ? "New support ticket" : "New reply on a support ticket";
        var link = string.IsNullOrWhiteSpace(_emailOptions.AdminConsoleBaseUrl)
            ? null
            : $"{_emailOptions.AdminConsoleBaseUrl.TrimEnd('/')}/support/tickets/{ticket.Id}";

        static string E(string value) => WebUtility.HtmlEncode(value);

        var linkHtml = link is null
            ? "<p>Open the Crownus admin console to reply.</p>"
            : $"<p><a href=\"{E(link)}\">Open the ticket in the Crownus admin console</a></p>";

        return $"""
            <p><strong>{E(heading)}</strong></p>
            <p>Tenant: {E(tenantName)}<br/>From: {E(actorName)}<br/>Priority: {E(ticket.Priority)}</p>
            <p>Subject: {E(ticket.Subject)}</p>
            <blockquote style="white-space:pre-wrap">{E(Truncate(text, ExcerptLength))}</blockquote>
            {linkHtml}
            """;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}
