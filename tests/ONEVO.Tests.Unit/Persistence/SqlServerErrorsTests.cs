using ONEVO.Infrastructure.Persistence.SqlServer;
using Xunit;

namespace ONEVO.Tests.Unit.Persistence;

public class SqlServerErrorsTests
{
    [Theory]
    [InlineData(2601, true)]
    [InlineData(2627, true)]
    [InlineData(547, false)]
    [InlineData(1205, false)]
    public void IsUniqueViolationNumber_MatchesBothUniqueErrors(int number, bool expected)
        => Assert.Equal(expected, SqlServerErrors.IsUniqueViolationNumber(number));

    [Fact]
    public void ParseConstraintName_ReadsUniqueIndexMessage()
    {
        const string message =
            "Cannot insert duplicate key row in object 'dbo.positions' with unique index " +
            "'ix_positions_coverage_order'. The duplicate key value is (1, 2).";

        Assert.Equal("ix_positions_coverage_order", SqlServerErrors.ParseConstraintName(message));
    }

    [Fact]
    public void ParseConstraintName_ReadsUniqueConstraintMessage()
    {
        const string message =
            "Violation of UNIQUE KEY constraint 'uq_users_email'. Cannot insert duplicate key in " +
            "object 'dbo.users'. The duplicate key value is (a@b.test).";

        Assert.Equal("uq_users_email", SqlServerErrors.ParseConstraintName(message));
    }

    [Fact]
    public void ParseConstraintName_ReadsForeignKeyMessage()
    {
        const string message =
            "The INSERT statement conflicted with the FOREIGN KEY constraint \"fk_tasks_project\". " +
            "The conflict occurred in database \"crownus\", table \"dbo.projects\", column 'id'.";

        Assert.Equal("fk_tasks_project", SqlServerErrors.ParseConstraintName(message));
    }

    [Fact]
    public void ParseConstraintName_IgnoresValuesContainingQuotesAfterTheName()
    {
        const string message =
            "Cannot insert duplicate key row in object 'dbo.users' with unique index 'ix_users_email'. " +
            "The duplicate key value is (o'brien@b.test).";

        Assert.Equal("ix_users_email", SqlServerErrors.ParseConstraintName(message));
    }

    [Fact]
    public void ParseConstraintName_ReturnsNullWhenNoNameIsPresent()
        => Assert.Null(SqlServerErrors.ParseConstraintName("Transaction was deadlocked."));

    [Fact]
    public void Classifiers_ReturnFalse_WhenNoSqlExceptionIsInTheChain()
    {
        var exception = new InvalidOperationException("outer", new TimeoutException("inner"));

        Assert.False(SqlServerErrors.IsUniqueViolation(exception));
        Assert.False(SqlServerErrors.IsForeignKeyViolation(exception));
        Assert.False(SqlServerErrors.IsDeadlock(exception));
        Assert.False(SqlServerErrors.IsLockTimeout(exception));
        Assert.False(SqlServerErrors.IsRlsBlock(exception));
        Assert.Null(SqlServerErrors.ConstraintName(exception));
    }
}
