using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
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
        Materializer = new MeetingOccurrenceMaterializer(Meetings, Writer,
            new MeetingTaskDefaults(new EfTaskCategoryRepository(db), new EfTaskStatusRepository(db)), Projects, Recurrence, UnitOfWork, Links);
        Options = Microsoft.Extensions.Options.Options.Create(new MeetingsOptions { Create = true });
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
