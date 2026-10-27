using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.OutboxHandlers;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Domain.Features.OrgStructure.Entities;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using ONEVO.Domain.Lookups;
using ONEVO.Infrastructure.Configuration;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.Calendar;
using ONEVO.Infrastructure.Security;
using ONEVO.Infrastructure.Services.SharedPlatform.Outbox;
using ONEVO.Tests.Integration.Support;
using EmployeeRepository = ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeRepository;

namespace ONEVO.Tests.Integration.Calendar;

/// <summary>Real PostgreSQL, real repositories, real OutboxWriter and CalendarNotificationSender.
/// Handlers are built per call on a fresh context, the way a request scope would build them.</summary>
public sealed class CalendarNotificationDb
{
    public sealed class StubCurrentUser(Guid tenantId, Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
        public Guid TenantId => tenantId;
        public string Email => "organizer@calendar-test.onevo.dev";
        public IReadOnlyList<string> Permissions => [];
        public bool HasPermission(string permission) => false;
        public bool IsAuthenticated => true;
    }

    /// <summary>Delete never reaches these: the harness events have no online meeting.</summary>
    private sealed class UnusedProviderClients : ITeamsMeetingClient, IZoomMeetingClient, ICalendarConnectionTokenProvider
    {
        Task<TeamsMeetingDto> ITeamsMeetingClient.CreateOrGetMeetingAsync(string a, string externalId, string s, DateTimeOffset b, DateTimeOffset e, CancellationToken ct, IReadOnlyList<string>? attendees) => throw new InvalidOperationException("not used");
        Task ITeamsMeetingClient.CancelMeetingAsync(string a, string id, CancellationToken ct) => throw new InvalidOperationException("not used");
        Task<IReadOnlyList<TeamsAttendanceRecordDto>> ITeamsMeetingClient.GetAttendanceAsync(string a, string id, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) => throw new InvalidOperationException("not used");
        Task<ZoomMeetingDto> IZoomMeetingClient.CreateMeetingAsync(string a, string s, DateTimeOffset b, DateTimeOffset e, string correlationToken, CancellationToken ct, IReadOnlyList<string>? attendees) => throw new InvalidOperationException("not used");
        Task<ZoomMeetingDto?> IZoomMeetingClient.FindMeetingByCorrelationTokenAsync(string a, string t, DateTimeOffset s, CancellationToken ct) => throw new InvalidOperationException("not used");
        Task IZoomMeetingClient.CancelMeetingAsync(string a, string id, CancellationToken ct) => throw new InvalidOperationException("not used");
        Task<IReadOnlyList<ZoomAttendanceRecordDto>> IZoomMeetingClient.GetAttendanceAsync(string a, string id, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) => throw new InvalidOperationException("not used");
        Task<string?> ICalendarConnectionTokenProvider.GetFreshAccessTokenAsync(ExternalCalendarConnection c, string p, CancellationToken ct) => throw new InvalidOperationException("not used");
        Task<string?> ICalendarConnectionTokenProvider.ForceRefreshAccessTokenAsync(ExternalCalendarConnection c, string p, CancellationToken ct) => throw new InvalidOperationException("not used");
    }

    public static readonly AesEncryptionService Encryption = new(
        Options.Create(new EncryptionOptions { MasterKey = "integration-test-master-key-32-chars-min" }));

    private readonly SystemDateTimeProvider _clock = new();
    private string _connectionString = string.Empty;

    public Guid TenantId { get; private set; }
    public Employee Organizer { get; private set; } = null!;
    public StubCurrentUser OrganizerUser => new(TenantId, Organizer.UserId);
    public object Unused { get; } = new UnusedProviderClients();

    public static async Task<CalendarNotificationDb> CreateAsync()
    {
        var h = new CalendarNotificationDb { _connectionString = await SharedPostgresTemplate.CreateDatabaseAsync() };
        h.TenantId = Guid.NewGuid();
        h.Organizer = NewEmployee(h.TenantId, "Ada", "Owner");
        await using var db = h.Context(new StubCurrentUser(h.TenantId, h.Organizer.UserId));
        if (!await db.EmploymentStatuses.AnyAsync(s => s.Id == 1))
            db.EmploymentStatuses.Add(new EmploymentStatus { Id = 1, Code = "active", Label = "Active" });
        db.Tenants.Add(new Tenant
        {
            Id = h.TenantId, Name = "Calendar Tenant", Slug = $"cal-{Guid.NewGuid():N}"[..20],
            CompanySizeRange = "1-10", Status = TenantStatus.Active
        });
        db.Employees.Add(h.Organizer);
        await db.SaveChangesAsync();
        return h;
    }

    private static Employee NewEmployee(Guid tenantId, string first, string last) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, UserId = Guid.NewGuid(),
        EmployeeNumber = $"CAL-{Guid.NewGuid():N}"[..12], FirstName = first, LastName = last,
        Email = $"{first.ToLowerInvariant()}.{Guid.NewGuid():N}@calendar-test.onevo.dev",
        HireDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1), EmploymentStatusId = 1
    };

    public ApplicationDbContext Context(ICurrentUser? auditUser = null)
    {
        var tenantContext = new TenantContextAccessor();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantRlsInterceptor(tenantContext))
            .Options;
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(auditUser ?? OrganizerUser, _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            tenantContext);
    }

    public async Task<Employee> AddEmployeeAsync(string first, string last)
    {
        await using var db = Context();
        var employee = NewEmployee(TenantId, first, last);
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee;
    }

    /// <summary>The organizer's event with the given participants and guests, created directly.</summary>
    public async Task<CalendarEvent> AddEventAsync(IReadOnlyList<Employee> participants, IReadOnlyList<string> guestEmails, bool recurring = false)
    {
        await using var db = Context();
        var start = DateTimeOffset.UtcNow.Date.AddDays(3).AddHours(9);
        var calendarEvent = new CalendarEvent
        {
            Id = Guid.NewGuid(), TenantId = TenantId, Title = "Quarterly review", StartDate = new DateTimeOffset(start, TimeSpan.Zero),
            EndDate = new DateTimeOffset(start.AddHours(1), TimeSpan.Zero), SourceType = CalendarEventSourceTypes.Manual,
            Recurrence = recurring ? CalendarRecurrences.Weekly : CalendarRecurrences.None,
            RecurrenceRule = recurring ? "FREQ=WEEKLY" : null
        };
        db.PersonalCalendarEvents.Add(calendarEvent);
        foreach (var participant in participants)
            db.CalendarEventParticipants.Add(new CalendarEventParticipant
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EventId = calendarEvent.Id, EmployeeId = participant.Id,
                ResponseStatus = CalendarEventParticipantStatuses.Pending
            });
        foreach (var email in guestEmails)
            db.CalendarEventGuests.Add(new CalendarEventGuest { Id = Guid.NewGuid(), TenantId = TenantId, EventId = calendarEvent.Id, Email = email });
        await db.SaveChangesAsync();
        return calendarEvent;
    }

    public sealed record CalendarHandlers(
        ApplicationDbContext Db, EfCalendarEventRepository Events, EmployeeRepository Employees,
        CalendarNotificationSender Sender, UnitOfWork UnitOfWork) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    /// <summary>One "request scope": a context and the real collaborators handlers depend on.</summary>
    public CalendarHandlers Scope(ICurrentUser? caller = null)
    {
        var db = Context(caller ?? OrganizerUser);
        var employees = new EmployeeRepository(db);
        var sender = new CalendarNotificationSender(new OutboxWriter(db, Encryption, _clock), employees);
        return new CalendarHandlers(db, new EfCalendarEventRepository(db), employees, sender, new UnitOfWork(db));
    }

    public sealed record OutboxRow(string Type, string? DeduplicationKey, JsonDocument Payload);

    public async Task<IReadOnlyList<OutboxRow>> OutboxAsync()
    {
        await using var db = Context();
        var rows = await db.Set<OutboxMessage>().AsNoTracking().Where(m => m.TenantId == TenantId).OrderBy(m => m.CreatedAt).ToListAsync();
        return rows.Select(m => new OutboxRow(m.Type, m.DeduplicationKey, JsonDocument.Parse(Encryption.Decrypt(m.EncryptedPayload)))).ToList();
    }

    public static IEnumerable<OutboxRow> Emails(IEnumerable<OutboxRow> rows, string kind) =>
        rows.Where(r => r.Type == OutboxMessageTypes.CalendarEventInviteEmail
            && (r.Payload.RootElement.TryGetProperty("Kind", out var k) ? k.GetString() : CalendarEventEmailKinds.Invite) == kind);

    public static IEnumerable<OutboxRow> InApp(IEnumerable<OutboxRow> rows, string templateCode) =>
        rows.Where(r => r.Type == OutboxMessageTypes.WorkNotification && r.Payload.RootElement.GetProperty("TemplateCode").GetString() == templateCode);
}
