using FluentAssertions;
using ONEVO.Infrastructure.Persistence.Repositories.OrgStructure;
using ONEVO.Tests.Integration.Support;
using Xunit;

namespace ONEVO.Tests.Integration.MyTeam;

public sealed class DepartmentDescendantsIntegrationTests
{
    [Fact]
    public async Task Multi_root_query_returns_union_of_active_descendants_in_one_command()
    {
        var helper = await MyTeamDb.CreateAsync();
        Guid a, a1, a11, b, b1, b11, c;
        await using (var db = helper.NewContext())
        {
            a = helper.AddDepartment(db);
            a1 = helper.AddDepartment(db, a);
            a11 = helper.AddDepartment(db, a1);
            b = helper.AddDepartment(db);
            b1 = helper.AddDepartment(db, b, active: false); // inactive intermediate truncates the walk
            b11 = helper.AddDepartment(db, b1);
            c = helper.AddDepartment(db);
            await db.SaveChangesAsync();
        }

        var counter = new CountingDbCommandInterceptor();
        await using var read = helper.NewContext(counter);
        var repository = new EfDepartmentRepository(read);

        var result = await repository.GetDescendantDepartmentIdsAsync(
            helper.TenantId, helper.LegalEntityId, new[] { a, b, c });

        result.Should().BeEquivalentTo(new[] { a1, a11 });
        result.Should().NotContain(new[] { b1, b11, a, b, c });
        counter.Count.Should().Be(1);
    }

    [Fact]
    public async Task Empty_root_set_returns_empty_without_querying()
    {
        var helper = await MyTeamDb.CreateAsync();
        var counter = new CountingDbCommandInterceptor();
        await using var read = helper.NewContext(counter);

        var result = await new EfDepartmentRepository(read)
            .GetDescendantDepartmentIdsAsync(helper.TenantId, helper.LegalEntityId, Array.Empty<Guid>());

        result.Should().BeEmpty();
        counter.Count.Should().Be(0);
    }
}
