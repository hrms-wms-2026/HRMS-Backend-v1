using EmployeeEntity = ONEVO.Domain.Features.CoreHr.Entities.Employee;
using Xunit;

namespace ONEVO.Tests.Unit.Features.CoreHr.Employee;

public class EmployeeWorkModeRetargetTests
{
    [Fact]
    public void WorkModeId_IsNullableGuid_NotInt()
    {
        var employee = new EmployeeEntity();
        // Compiles only once WorkModeId is Guid? - this test's real assertion is the compiler.
        employee.WorkModeId = Guid.NewGuid();
        Assert.NotNull(employee.WorkModeId);

        employee.WorkModeId = null;
        Assert.Null(employee.WorkModeId);
    }
}
