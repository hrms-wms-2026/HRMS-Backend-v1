using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Meetings;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Services.WorkManagement.Meetings;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>Meetings:BulkInvite fans one invite out to many outbox messages, which is only safe once the outbox claims
/// messages atomically (P-1, spec 9); the app refuses to start with the flag on and P-1 missing.</summary>
public sealed class MeetingsStartupCheckIntegrationTests
{
    private static MeetingsStartupCheck CheckFor(MyTeamDb h, bool bulkInvite)
    {
        var scopes = new ServiceCollection()
            .AddScoped<TenantContextAccessor>()
            .AddScoped<IWritableTenantContext>(sp => sp.GetRequiredService<TenantContextAccessor>())
            .AddScoped<ApplicationDbContext>(_ => h.NewContext())
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        return new MeetingsStartupCheck(scopes, Options.Create(new MeetingsOptions { BulkInvite = bulkInvite }));
    }

    /// <summary>P-1's migration (AddOutboxClaimLease) is part of this branch, so the test database has the claim
    /// column; dropping it gives a database P-1 has not reached.</summary>
    private static async Task WithoutP1Async(MyTeamDb h)
    {
        await using var connection = new NpgsqlConnection(h.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("ALTER TABLE outbox_messages DROP COLUMN locked_by", connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task BulkInviteWithoutP1_FailsStartup()
    {
        var h = await MyTeamDb.CreateAsync();
        await WithoutP1Async(h);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CheckFor(h, bulkInvite: true).StartAsync(CancellationToken.None));

        Assert.Equal("Meetings:BulkInvite needs the outbox claim fix (P-1)", error.Message);
    }

    [Fact]
    public async Task BulkInviteWithP1_Starts()
    {
        var h = await MyTeamDb.CreateAsync();

        await CheckFor(h, bulkInvite: true).StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task BulkInviteOff_StartsWithoutP1()
    {
        var h = await MyTeamDb.CreateAsync();
        await WithoutP1Async(h);

        await CheckFor(h, bulkInvite: false).StartAsync(CancellationToken.None);
    }
}
