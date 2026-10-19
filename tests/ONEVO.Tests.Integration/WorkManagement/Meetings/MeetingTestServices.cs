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
        Materializer = new MeetingOccurrenceMaterializer(Meetings, Writer,
            new MeetingTaskDefaults(new EfTaskCategoryRepository(db), new EfTaskStatusRepository(db)), Projects, Recurrence, UnitOfWork);
        Options = Microsoft.Extensions.Options.Options.Create(new MeetingsOptions { Create = true });
        Splitter = new MeetingSeriesSplitter(Meetings, Writer, Materializer, Recurrence, UnitOfWork, clock);
        Edits = new MeetingEditService(Options, Meetings, Writer, Materializer, Access, UnitOfWork, clock, Splitter, Recurrence);
        Cancels = new MeetingCancelService(Options, Meetings, Writer, Materializer, Access, UnitOfWork, clock);
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
}
