using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

public class RoleTemplateSeederLeavePermissionTests
{
    [Fact]
    public void HrManagerTemplate_IncludesLeaveManage()
    {
        var permissions = ONEVO.Infrastructure.Persistence.Seeders.RoleTemplateSeeder
            .HrManagerPermissionCodesForTest();

        Assert.Contains("leave:manage", permissions);
        Assert.Contains("leave:read", permissions);
        Assert.Contains("leave:approve", permissions);
    }
}
