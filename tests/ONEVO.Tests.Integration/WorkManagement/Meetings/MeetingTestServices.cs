using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Meetings;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement.Meetings;
using ONEVO.Infrastructure.Services.WorkManagement.Meetings;

namespace ONEVO.Tests.Integration.WorkManagement.Meetings;

/// <summary>A fixed clock for meeting tests.</summary>
public sealed class MeetingTestClock(DateTimeOffset now) : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = now;
    public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
}

/// <summary>The real meeting services over one context (Meetings:Create on), for service-level integration tests.</summary>
public sealed class MeetingTestServices
{
    public MeetingTestServices(ApplicationDbContext db, IDateTimeProvider clock)
    {
        Db = db;
        Clock = clock;
        Meetings = new EfMeetingRepository(db);
        Writer = new EfMeetingOccurrenceWriter(db);
        UnitOfWork = new UnitOfWork(db);
        Recurrence = new IcalNetMeetingRecurrence();
        Projects = new EfProjectRepository(db);
        Access = new MeetingAccessResolver(Meetings);
        Links = new EfMeetingLinkRepository(db);
        Options = Microsoft.Extensions.Options.Options.Create(new MeetingsOptions { Create = true });
        Materializer = new MeetingOccurrenceMaterializer(Meetings, Writer,
            new MeetingTaskDefaults(new EfTaskCategoryRepository(db), new EfTaskStatusRepository(db)), Projects, Recurrence, UnitOfWork, Links,
            Options);
        Notifier = new MeetingNotifier(new ONEVO.Application.Features.Calendar.Services.CalendarNotificationSender(
                new ONEVO.Infrastructure.Services.SharedPlatform.Outbox.OutboxWriter(db, ONEVO.Tests.Integration.Calendar.CalendarNotificationDb.Encryption, clock),
                new ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeRepository(db)),
            Meetings, Links, new EfEmployeeRepository(db), clock);
        Splitter = new MeetingSeriesSplitter(Meetings, Writer, Materializer, Recurrence, UnitOfWork, clock, Links);
        Edits = new MeetingEditService(Options, Meetings, Writer, Materializer, Access, UnitOfWork, clock, Splitter, Recurrence, Notifier);
        Cancels = new MeetingCancelService(Options, Meetings, Writer, Materializer, Access, UnitOfWork, clock, Notifier, LinkService(new NoProvisioner()));
        Attendees = new MeetingAttendeeService(Options, Meetings, Writer, Materializer, Access, new EfEmployeeRepository(db), UnitOfWork, clock, Notifier);
        Factory = new MeetingSeriesFactory(Meetings, Writer, new MeetingTaskDefaults(new EfTaskCategoryRepository(db), new EfTaskStatusRepository(db)),
            Projects, new EfEmployeeRepository(db), Recurrence, UnitOfWork, clock, Materializer, Notifier,
            new ONEVO.Infrastructure.Persistence.Repositories.Calendar.EfExternalCalendarConnectionRepository(db), Links, LinkService(new NoProvisioner()));
    }

    /// <summary>The series link service over these services, with the given provider seam (and meetings options).</summary>
    public SeriesMeetingLinkService LinkService(ONEVO.Application.Features.Calendar.Services.IMeetingProvisioner provisioner,
        MeetingsOptions? options = null)
        => new(Links, Meetings, Writer, new ONEVO.Infrastructure.Persistence.Repositories.Calendar.EfExternalCalendarConnectionRepository(Db),
            provisioner, Notifier, Recurrence, new EfEmployeeRepository(Db), Outbox, UnitOfWork, Clock, Access,
            options is null ? Options : Microsoft.Extensions.Options.Options.Create(options));

    /// <summary>The per-occurrence attendance sync over these services, with the given provider session client.</summary>
    public MeetingAttendanceSyncService AttendanceSync(ONEVO.Application.Features.Calendar.ServiceInterfaces.IMeetingAttendanceSessionClient client,
        MeetingsOptions? options = null)
        => new(new EfMeetingAttendanceRepository(Db), Links, Meetings, Access,
            new ONEVO.Infrastructure.Persistence.Repositories.Calendar.EfExternalCalendarConnectionRepository(Db), new TokenStub(), [client],
            new ONEVO.Application.Features.Calendar.Services.CalendarNotificationSender(Outbox,
                new ONEVO.Infrastructure.Persistence.Repositories.CoreHr.EfEmployeeRepository(Db)),
            UnitOfWork, options is null ? Options : Microsoft.Extensions.Options.Options.Create(options), Clock);

    /// <summary>The occurrence attendance reads over these services.</summary>
    /// <summary>The D5 approval snapshot builder. Its tree load reads only objectives, so the hierarchy service needs no
    /// membership coordinator here.</summary>
    public MeetingApprovalSnapshotBuilder Snapshots =>
        new(new EfEmployeeRepository(Db), new WorkHierarchyService(new EfObjectiveRepository(Db), null!), Meetings, Links);

    public MeetingAttendanceReadService AttendanceReads => new(Meetings, Access, new EfMeetingAttendanceRepository(Db), Links, new EfEmployeeRepository(Db));

    /// <summary>Every connection has a fresh token.</summary>
    private sealed class TokenStub : ONEVO.Application.Features.Calendar.ServiceInterfaces.ICalendarConnectionTokenProvider
    {
        public Task<string?> GetFreshAccessTokenAsync(ONEVO.Domain.Features.Calendar.Entities.ExternalCalendarConnection connection,
            string oauthProvider, CancellationToken ct) => Task.FromResult<string?>("token");
        public Task<string?> ForceRefreshAccessTokenAsync(ONEVO.Domain.Features.Calendar.Entities.ExternalCalendarConnection connection,
            string oauthProvider, CancellationToken ct) => Task.FromResult<string?>("token");
    }

    public ONEVO.Infrastructure.Services.SharedPlatform.Outbox.OutboxWriter Outbox => new(Db, ONEVO.Tests.Integration.Calendar.CalendarNotificationDb.Encryption, Clock);

    /// <summary>Provisioning is queued, never called, through the factory.</summary>
    private sealed class NoProvisioner : ONEVO.Application.Features.Calendar.Services.IMeetingProvisioner
    {
        public Task<ONEVO.Application.Features.Calendar.Services.MeetingProvisioningOutcome> ProvisionAsync(
            ONEVO.Application.Features.Calendar.Services.MeetingProvisioningRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ResolveForRemovalAsync(ONEVO.Application.Features.Calendar.Services.MeetingProvisioningRequest request, CancellationToken ct)
            => throw new NotSupportedException();
    }

    public ApplicationDbContext Db { get; }
    public IDateTimeProvider Clock { get; }
    public EfMeetingRepository Meetings { get; }
    public EfMeetingOccurrenceWriter Writer { get; }
    public UnitOfWork UnitOfWork { get; }
    public IcalNetMeetingRecurrence Recurrence { get; }
    public EfProjectRepository Projects { get; }
    public MeetingAccessResolver Access { get; }
    public MeetingOccurrenceMaterializer Materializer { get; }
    public IOptions<MeetingsOptions> Options { get; }
    public MeetingSeriesSplitter Splitter { get; }
    public MeetingEditService Edits { get; }
    public MeetingCancelService Cancels { get; }
    public MeetingAttendeeService Attendees { get; }
    public MeetingSeriesFactory Factory { get; }
    public EfMeetingLinkRepository Links { get; }
    public MeetingNotifier Notifier { get; }
}
