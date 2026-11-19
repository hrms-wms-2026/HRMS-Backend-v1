using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ONEVO.Tests.Integration.Support;
using Xunit;

namespace ONEVO.Tests.Integration.MyTeam;

public sealed class MyTeamDbSmokeTests
{
    [Fact]
    public async Task Seeds_and_counts_commands()
    {
        var helper = await MyTeamDb.CreateAsync();
        await using (var db = helper.NewContext())
        {
            helper.AddEmployee(db);
            await db.SaveChangesAsync();
        }

        var counter = new CountingDbCommandInterceptor();
        await using var read = helper.NewContext(counter);
        (await read.Employees.CountAsync(e => e.TenantId == helper.TenantId)).Should().Be(1);
        counter.Count.Should().Be(1);
    }
}
