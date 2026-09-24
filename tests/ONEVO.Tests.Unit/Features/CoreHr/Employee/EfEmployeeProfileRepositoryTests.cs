using Microsoft.EntityFrameworkCore;
using Moq;
using MediatR;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.CoreHr;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class EfEmployeeProfileRepositoryTests
{
    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<IPublisher>();

        var auditInterceptor = new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object);
        var softDeleteInterceptor = new SoftDeleteInterceptor(dateTimeProvider.Object);
        var domainEventInterceptor = new DomainEventDispatchInterceptor(publisher.Object);

        return new ApplicationDbContext(options, auditInterceptor, softDeleteInterceptor, domainEventInterceptor, new Mock<ITenantContext>().Object);
    }

    [Fact]
    public async Task ReplaceAddresses_RemovesOldRowsAndInsertsNewOnes()
    {
        await using var db = BuildInMemoryDb();
        var repo = new EfEmployeeProfileRepository(db);
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        db.EmployeeAddresses.Add(new EmployeeAddress
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
            AddressType = "current", AddressJson = "{}", IsPrimary = true,
            CreatedById = employeeId, CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var replacement = new[]
        {
            new EmployeeAddress
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
                AddressType = "permanent", AddressJson = "{\"city\":\"Colombo\"}", IsPrimary = true,
                CreatedById = employeeId, CreatedAt = DateTimeOffset.UtcNow
            }
        };

        repo.ReplaceAddresses(tenantId, employeeId, replacement);
        await repo.SaveChangesAsync();

        var stored = await repo.ListAddressesAsync(tenantId, employeeId);
        Assert.Single(stored);
        Assert.Equal("permanent", stored[0].AddressType);
    }

    [Fact]
    public async Task AddAndGetEmergencyContact_RoundTrips()
    {
        await using var db = BuildInMemoryDb();
        var repo = new EfEmployeeProfileRepository(db);
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var contact = new EmployeeEmergencyContact
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
            Name = "Jane Doe", Relationship = "spouse", Phone = "555-1111", IsPrimary = true,
            CreatedById = employeeId, CreatedAt = DateTimeOffset.UtcNow
        };

        await repo.AddEmergencyContactAsync(contact);
        await repo.SaveChangesAsync();

        var fetched = await repo.GetEmergencyContactAsync(tenantId, employeeId, contact.Id);
        Assert.NotNull(fetched);
        Assert.Equal("Jane Doe", fetched!.Name);
    }

    [Fact]
    public async Task GetPrimaryBankDetail_ReturnsOnlyThePrimaryRow()
    {
        await using var db = BuildInMemoryDb();
        var repo = new EfEmployeeProfileRepository(db);
        var tenantId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        db.EmployeeBankDetails.Add(new EmployeeBankDetail
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
            BankName = "Old Bank", BranchName = "Branch", AccountHolderName = "Jane",
            AccountNumberEncrypted = "cipher-old", AccountType = "savings", IsPrimary = false,
            CreatedById = employeeId, CreatedAt = DateTimeOffset.UtcNow
        });
        db.EmployeeBankDetails.Add(new EmployeeBankDetail
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
            BankName = "New Bank", BranchName = "Branch", AccountHolderName = "Jane",
            AccountNumberEncrypted = "cipher-new", AccountType = "savings", IsPrimary = true,
            CreatedById = employeeId, CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var primary = await repo.GetPrimaryBankDetailAsync(tenantId, employeeId);

        Assert.NotNull(primary);
        Assert.Equal("New Bank", primary!.BankName);
    }
}
