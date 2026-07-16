using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity;
using ONEVO.Infrastructure.Persistence.Interceptors;

namespace ONEVO.Infrastructure.Persistence;

public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention();

        var dateTimeProvider = new SystemDateTimeProvider();
        var currentUser = new AnonymousCurrentUser();

        return new ApplicationDbContext(
            optionsBuilder.Options,
            new AuditableEntityInterceptor(currentUser, dateTimeProvider),
            new SoftDeleteInterceptor(dateTimeProvider),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor()); // System mode — no tenant filter during migrations
    }
}
