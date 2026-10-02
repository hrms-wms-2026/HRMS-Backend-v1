using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

/// <summary>Shared InMemory ApplicationDbContext builder for the WorkManagement repository-read
/// tests. Extracted from EmployeeWorkGraphRepositoryReadsTests so
/// EmployeeWorkActivityTaskRepositoryReadsTests can reuse it too.</summary>
internal static class EmployeeWorkGraphRepositoryReadsTestsDb
{
    /// <summary>Optional `clock` lets a caller control AuditableEntityInterceptor's `now` (e.g. via
    /// SetupSequence) - needed by any test whose repository read orders by CreatedAt/UpdatedAt, since
    /// the interceptor stamps UpdatedAt on every Added/Modified save, overwriting whatever the entity
    /// itself was constructed with.</summary>
    public static ApplicationDbContext Build(Mock<IDateTimeProvider>? clock = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var currentUser = new Mock<ICurrentUser>();
        clock ??= new Mock<IDateTimeProvider>();
        var publisher = new Mock<MediatR.IPublisher>();
        var tenantContext = new Mock<ITenantContext>();
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(currentUser.Object, clock.Object),
            new SoftDeleteInterceptor(clock.Object),
            new DomainEventDispatchInterceptor(publisher.Object),
            tenantContext.Object);
    }
}
