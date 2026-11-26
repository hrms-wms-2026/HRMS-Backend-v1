using System.Reflection;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using ONEVO.Infrastructure.Persistence.SqlServer;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Infrastructure;

/// <summary>
/// A connection kept open for the request has no silent reconnect (ConnectRetryCount=0), so EF's retry must
/// reopen it. SqlClient's managed networking (Linux) reports a killed connection as number 0, class 20, which
/// EF's transient list does not cover; Windows reports 10054. Both must be retried.
/// </summary>
public sealed class BrokenConnectionRetryingExecutionStrategyTests
{
    [Theory]
    [InlineData(0, 20)]      // managed networking (Linux): "Connection was terminated" / internal exception
    [InlineData(10054, 20)]  // Windows native networking: connection forcibly closed
    [InlineData(0, 21)]
    public void A_fatal_error_that_closed_the_connection_is_retried(int number, byte errorClass)
    {
        BrokenConnectionRetryingExecutionStrategy.IsBrokenConnection(CreateSqlException(number, errorClass))
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(2627, 14)]   // duplicate key
    [InlineData(547, 16)]    // foreign key conflict
    [InlineData(33504, 16)]  // RLS block predicate
    public void A_statement_error_is_not_treated_as_a_broken_connection(int number, byte errorClass)
    {
        BrokenConnectionRetryingExecutionStrategy.IsBrokenConnection(CreateSqlException(number, errorClass))
            .Should().BeFalse();
    }

    [Fact]
    public void An_exception_that_is_not_from_sql_server_is_not_a_broken_connection()
    {
        BrokenConnectionRetryingExecutionStrategy.IsBrokenConnection(new InvalidOperationException("x"))
            .Should().BeFalse();
    }

    // SqlException has no public constructor; build one the way SqlClient does.
    private static SqlException CreateSqlException(int number, byte errorClass)
    {
        var error = (SqlError)typeof(SqlError)
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .Select(c => c.Invoke(c.GetParameters().Select(p => Arg(p, p.Name switch
            {
                "infoNumber" => number,
                "errorState" => 1,
                "errorClass" => errorClass,
                "server" => "test",
                "errorMessage" => "test error",
                "procedure" => "",
                "batchIndex" => -1,
                _ => null
            })).ToArray()))
            .First();

        var errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(errors, [error]);

        static object? Arg(ParameterInfo p, object? value) => value switch
        {
            null => p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null,
            string => value,
            _ => Convert.ChangeType(value, p.ParameterType)
        };

        return (SqlException)typeof(SqlException)
            .GetMethod("CreateException", BindingFlags.NonPublic | BindingFlags.Static, [typeof(SqlErrorCollection), typeof(string)])!
            .Invoke(null, [errors, "16.0"])!;
    }
}
