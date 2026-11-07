using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using ONEVO.Infrastructure.Persistence.Interceptors;

namespace ONEVO.Tests.Integration.Support;

/// <summary>
/// Opens test databases. A PostgreSQL superuser bypassed RLS, so tests seed through one; on SQL
/// Server every login is subject to RLS, including sa. An admin (sa) connection therefore gets
/// admin session context as it opens, the mode the app's own admin paths use. The app logins
/// (onevo_app, onevo_migrator) get nothing here and stay fully restricted.
/// </summary>
public static class TestDb
{
    /// <summary>Binds the next @p1, @p2... placeholder.</summary>
    public static SqlParameter AddPositional(this SqlParameterCollection parameters, object? value)
        => parameters.AddWithValue($"@p{parameters.Count + 1}", value ?? DBNull.Value);

    public static bool IsAdmin(string connectionString)
        => string.Equals(new SqlConnectionStringBuilder(connectionString).UserID, "sa", StringComparison.OrdinalIgnoreCase);

    public static SqlConnection CreateConnection(string connectionString)
    {
        var connection = new SqlConnection(connectionString);
        if (IsAdmin(connectionString))
        {
            connection.StateChange += (_, change) =>
            {
                if (change.CurrentState == ConnectionState.Open)
                {
                    using var command = TenantRlsInterceptor.SetTenantCommand(connection, string.Empty, "admin");
                    command.ExecuteNonQuery();
                }
            };
        }

        return connection;
    }

    public static DbContextOptionsBuilder<TContext> UseTestSqlServer<TContext>(
        this DbContextOptionsBuilder<TContext> options,
        string connectionString,
        Action<Microsoft.EntityFrameworkCore.Infrastructure.SqlServerDbContextOptionsBuilder>? configure = null)
        where TContext : DbContext
    {
        ((DbContextOptionsBuilder)options).UseTestSqlServer(connectionString, configure);
        return options;
    }

    public static DbContextOptionsBuilder UseTestSqlServer(
        this DbContextOptionsBuilder options,
        string connectionString,
        Action<Microsoft.EntityFrameworkCore.Infrastructure.SqlServerDbContextOptionsBuilder>? configure = null)
    {
        options.UseSqlServer(connectionString, configure);
        if (IsAdmin(connectionString))
        {
            options.AddInterceptors(new AdminContextInterceptor());
        }

        return options;
    }

    private sealed class AdminContextInterceptor : DbConnectionInterceptor
    {
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            using var command = TenantRlsInterceptor.SetTenantCommand(connection, string.Empty, "admin");
            command.ExecuteNonQuery();
        }

        public override async Task ConnectionOpenedAsync(
            DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            await using var command = TenantRlsInterceptor.SetTenantCommand(connection, string.Empty, "admin");
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
