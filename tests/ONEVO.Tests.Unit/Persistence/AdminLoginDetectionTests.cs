using ONEVO.Infrastructure.Configuration;
using Xunit;

namespace ONEVO.Tests.Unit.Persistence;

public class AdminLoginDetectionTests
{
    [Theory]
    [InlineData("Server=db;Database=x;User ID=sa;Password=p", true)]
    [InlineData("Server=db;Database=x;UID=SA;Password=p", true)]
    [InlineData("Server=db;Database=x;User ID=crownus_app;Password=p", false)]
    [InlineData("Server=db;Database=x;Integrated Security=True", false)]
    public void UsesAdminLogin_RecognisesSqlServerConnectionStrings(string connectionString, bool expected)
    {
        Assert.Equal(expected, ConfigurationStartupValidator.UsesAdminLogin(connectionString));
    }
}
