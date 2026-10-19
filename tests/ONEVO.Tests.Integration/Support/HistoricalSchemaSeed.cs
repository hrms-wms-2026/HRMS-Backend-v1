using Npgsql;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Tests.Integration.Support;

/// <summary>
/// Raw-SQL seeding for migration-upgrade tests that pin the schema at a historical migration.
/// EF's generated INSERT lists every column the compiled model maps, so once a later migration adds a
/// column (e.g. users.suspended_at) an EF SaveChanges at an older schema point references a column that
/// doesn't exist yet. These inserts name only the columns present since the table was created.
/// </summary>
internal static class HistoricalSchemaSeed
{
    /// <summary>
    /// Inserts users with exactly the NOT-NULL-without-default columns of the InitialSchema users table;
    /// later-added columns are nullable or computed (normalized_email), so Postgres fills them in.
    /// The connection must already be in admin mode (RLS bypass).
    /// </summary>
    public static async Task InsertUsersAsync(NpgsqlConnection connection, params User[] users)
    {
        foreach (var user in users)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO users (id, tenant_id, email, password_hash, first_name, last_name, is_active,
                                   email_verified, must_change_password, password_set_by_admin,
                                   created_at, created_by_id, is_deleted)
                VALUES (@id, @tenant, @email, @password_hash, @first_name, @last_name, @is_active,
                        @email_verified, @must_change_password, @password_set_by_admin,
                        @created_at, @created_by_id, @is_deleted);
                """;
            command.Parameters.AddWithValue("id", user.Id);
            command.Parameters.AddWithValue("tenant", user.TenantId);
            command.Parameters.AddWithValue("email", user.Email);
            command.Parameters.AddWithValue("password_hash", user.PasswordHash);
            command.Parameters.AddWithValue("first_name", user.FirstName);
            command.Parameters.AddWithValue("last_name", user.LastName);
            command.Parameters.AddWithValue("is_active", user.IsActive);
            command.Parameters.AddWithValue("email_verified", user.EmailVerified);
            command.Parameters.AddWithValue("must_change_password", user.MustChangePassword);
            command.Parameters.AddWithValue("password_set_by_admin", user.PasswordSetByAdmin);
            command.Parameters.AddWithValue("created_at", user.CreatedAt);
            command.Parameters.AddWithValue("created_by_id", user.CreatedById);
            command.Parameters.AddWithValue("is_deleted", user.IsDeleted);
            await command.ExecuteNonQueryAsync();
        }
    }
}
