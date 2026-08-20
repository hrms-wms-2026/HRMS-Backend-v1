using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;

namespace ONEVO.Tests.Integration.Support;

public static class PositionAssignmentRepositoryTestSupport
{
    private static readonly IDateTimeProvider Clock = new SystemDateTimeProvider();

    public static EfPositionAssignmentRepository CreateRepository(ApplicationDbContext db)
    {
        var closureRepo = new EfEmployeeHierarchyClosureRepository(db, Clock);
        return new EfPositionAssignmentRepository(db, closureRepo);
    }

    public static EfEmployeeHierarchyClosureRepository CreateClosureRepository(ApplicationDbContext db) =>
        new(db, Clock);
}
