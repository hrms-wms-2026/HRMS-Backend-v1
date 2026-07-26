using Moq;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;

namespace ONEVO.Tests.Unit.Features.Auth;

public class GlobalEmailDirectoryRepositoryContractTests
{
    [Fact]
    public async Task UpsertAsync_CanBeMocked()
    {
        var repo = new Mock<IGlobalEmailDirectoryRepository>();
        var tenantId = Guid.NewGuid();
        repo.Setup(r => r.UpsertAsync("user@example.com", tenantId, default))
            .Returns(Task.CompletedTask);

        await repo.Object.UpsertAsync("user@example.com", tenantId, default);

        repo.Verify(r => r.UpsertAsync("user@example.com", tenantId, default), Times.Once);
    }

    /// <summary>
    /// EF InMemory does not execute raw SQL, so ExecuteSqlInterpolatedAsync semantics cannot be
    /// verified via a database provider here. This guard instead asserts the repository source
    /// still contains the exact PostgreSQL upsert statement. True double-upsert (no duplicate
    /// row / no exception on repeat insert) proof requires a real PostgreSQL instance and is an
    /// integration-test concern, not a unit-test concern.
    /// </summary>
    [Fact]
    public void EfGlobalEmailDirectoryRepository_SourcePreservesPostgresUpsertStatement()
    {
        var path = FindRepositoryFile();
        var source = File.ReadAllText(path);

        Assert.Contains("ExecuteSqlInterpolatedAsync", source);
        Assert.Contains("INSERT INTO global_email_directory", source);
        Assert.Contains("ON CONFLICT (email, tenant_id) DO NOTHING", source);
    }

    private static string FindRepositoryFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "ONEVO.Infrastructure",
                "Persistence",
                "Repositories",
                "Auth",
                "Login",
                "EfGlobalEmailDirectoryRepository.cs");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate EfGlobalEmailDirectoryRepository.cs walking up from " + AppContext.BaseDirectory);
    }
}
