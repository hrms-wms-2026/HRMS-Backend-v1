using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.Commands.AddSupportTicketComment;
using ONEVO.Application.Features.DevPlatform.Support.Commands.CreatePlatformAnnouncement;
using ONEVO.Application.Features.DevPlatform.Support.Commands.CreateSupportTicket;
using ONEVO.Application.Features.DevPlatform.Support.Commands.PublishPlatformAnnouncement;
using ONEVO.Application.Features.DevPlatform.Support.Commands.UnpublishPlatformAnnouncement;
using ONEVO.Application.Features.DevPlatform.Support.Commands.UpdateSupportTicketStatus;
using ONEVO.Application.Features.DevPlatform.Support.Queries.GetSupportTicketDetail;
using ONEVO.Application.Features.DevPlatform.Support.Queries.ListSupportTickets;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Support;
using ONEVO.Tests.Integration.Support;
using ONEVO.Tests.Integration.Tenancy;
using Testcontainers.PostgreSql;
using Xunit;

namespace ONEVO.Tests.Integration.DevPlatform;

[Collection(WebApplicationFactoryCollection.Name)]
public sealed class SupportCenterIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("onevo_support_center_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private IntegrationTestEnvironmentScope _environmentScope = null!;
    private AdminTestFactory _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var connectionString = _postgres.GetConnectionString();

        await AdminTestFactory.MigrateDatabaseAsync(connectionString);
        _environmentScope = new IntegrationTestEnvironmentScope(connectionString);

        _factory = new AdminTestFactory(connectionString);
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        await _postgres.DisposeAsync();
        await _environmentScope.DisposeAsync();
    }

    [Fact]
    public async Task Ticket_create_then_list_then_detail_then_status_update_then_comment_all_persist_real_rows()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync(
            "SET session_replication_role = replica; " +
            "SELECT set_config('app.tenant_context_mode', 'admin', false);");

        var platformUser = new PlatformUser { Id = Guid.NewGuid(), Email = "agent@onevo.test", FullName = "Agent" };
        db.PlatformUsers.Add(platformUser);

        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Acme", Slug = $"acme-{Guid.NewGuid():N}" };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var ticketRepository = new EfSupportTicketRepository(db);

        var createHandler = new CreateSupportTicketCommandHandler(ticketRepository, uow, clock);
        var created = await createHandler.Handle(
            new CreateSupportTicketCommand(
                tenant.Id, "Cannot access dashboard", "Getting a 500 error.", SupportTicket.PriorityHigh, "bug",
                platformUser.Id),
            default);
        Assert.True(created.IsSuccess, created.Error);
        var ticketId = created.Value!.Id;

        var listHandler = new ListSupportTicketsQueryHandler(ticketRepository);
        var listed = await listHandler.Handle(new ListSupportTicketsQuery(null, null, tenant.Id, 1, 25), default);
        Assert.True(listed.IsSuccess, listed.Error);
        Assert.Contains(listed.Value!.Items, t => t.Id == ticketId);

        var detailHandler = new GetSupportTicketDetailQueryHandler(ticketRepository);
        var detail = await detailHandler.Handle(new GetSupportTicketDetailQuery(ticketId), default);
        Assert.True(detail.IsSuccess, detail.Error);
        Assert.Empty(detail.Value!.Comments);

        var statusHandler = new UpdateSupportTicketStatusCommandHandler(ticketRepository, uow, clock);
        var resolved = await statusHandler.Handle(
            new UpdateSupportTicketStatusCommand(ticketId, SupportTicket.StatusResolved), default);
        Assert.True(resolved.IsSuccess, resolved.Error);
        Assert.NotNull(resolved.Value!.ResolvedAt);

        var commentHandler = new AddSupportTicketCommentCommandHandler(ticketRepository, uow, clock);
        var commented = await commentHandler.Handle(
            new AddSupportTicketCommentCommand(ticketId, "Fixed by redeploying.", false, platformUser.Id), default);
        Assert.True(commented.IsSuccess, commented.Error);

        var detailAfterComment = await detailHandler.Handle(new GetSupportTicketDetailQuery(ticketId), default);
        Assert.True(detailAfterComment.IsSuccess, detailAfterComment.Error);
        Assert.Single(detailAfterComment.Value!.Comments);
        Assert.Equal("Fixed by redeploying.", detailAfterComment.Value!.Comments[0].Body);
        Assert.Equal(SupportTicket.StatusResolved, detailAfterComment.Value!.Ticket.Status);

        var rowInDb = await db.SupportTickets.FirstAsync(t => t.Id == ticketId);
        Assert.Equal(SupportTicket.StatusResolved, rowInDb.Status);
        var commentRowInDb = await db.SupportTicketComments.FirstAsync(c => c.TicketId == ticketId);
        Assert.Equal("Fixed by redeploying.", commentRowInDb.Body);
    }

    [Fact]
    public async Task Announcement_create_then_publish_then_unpublish_keeps_publishedAt_history()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync(
            "SET session_replication_role = replica; " +
            "SELECT set_config('app.tenant_context_mode', 'admin', false);");

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var announcementRepository = new EfPlatformAnnouncementRepository(db);

        var createHandler = new CreatePlatformAnnouncementCommandHandler(announcementRepository, uow, clock);
        var created = await createHandler.Handle(
            new CreatePlatformAnnouncementCommand("Scheduled maintenance", "Down for 1h on Saturday.", "warning", "all"),
            default);
        Assert.True(created.IsSuccess, created.Error);
        var announcementId = created.Value!.Id;
        Assert.False(created.Value!.IsPublished);

        var publishHandler = new PublishPlatformAnnouncementCommandHandler(announcementRepository, uow, clock);
        var published = await publishHandler.Handle(new PublishPlatformAnnouncementCommand(announcementId), default);
        Assert.True(published.IsSuccess, published.Error);
        Assert.True(published.Value!.IsPublished);
        Assert.NotNull(published.Value!.PublishedAt);
        var firstPublishedAt = published.Value!.PublishedAt;

        var unpublishHandler = new UnpublishPlatformAnnouncementCommandHandler(announcementRepository, uow, clock);
        var unpublished = await unpublishHandler.Handle(new UnpublishPlatformAnnouncementCommand(announcementId), default);
        Assert.True(unpublished.IsSuccess, unpublished.Error);
        Assert.False(unpublished.Value!.IsPublished);
        Assert.Equal(firstPublishedAt, unpublished.Value!.PublishedAt);

        var rowInDb = await db.PlatformAnnouncements.FirstAsync(a => a.Id == announcementId);
        Assert.False(rowInDb.IsPublished);
        Assert.Equal(firstPublishedAt, rowInDb.PublishedAt);
    }
}
