using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings;
using ONEVO.Infrastructure.Persistence;

namespace ONEVO.Infrastructure.Services.WorkManagement.Meetings;

/// <summary>
/// Refuses to start when a Meetings flag is on without what it depends on. Today: Meetings:BulkInvite fans one invite
/// out to many outbox messages, which is only safe once the outbox claims messages atomically (P-1, spec 9) - detected
/// by P-1's outbox_messages.locked_by column.
/// </summary>
public sealed class MeetingsStartupCheck : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptions<MeetingsOptions> _options;

    public MeetingsStartupCheck(IServiceScopeFactory scopes, IOptions<MeetingsOptions> options)
    {
        _scopes = scopes;
        _options = options;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Value.BulkInvite)
            return;

        await using var scope = _scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IWritableTenantContext>().SetAdminMode();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasClaimFix = await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT 1 FROM information_schema.columns
                           WHERE table_schema = current_schema() AND table_name = 'outbox_messages'
                             AND column_name = 'locked_by') AS "Value"
            """).ToListAsync(cancellationToken);
        if (!hasClaimFix.Single())
            throw new InvalidOperationException("Meetings:BulkInvite needs the outbox claim fix (P-1)");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
