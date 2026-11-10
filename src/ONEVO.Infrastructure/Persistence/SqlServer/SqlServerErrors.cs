using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace ONEVO.Infrastructure.Persistence.SqlServer;

/// <summary>
/// Classifies SQL Server failures by error number, looking through wrappers such as EF Core's
/// DbUpdateException, so callers never depend on a provider exception type's shape.
/// </summary>
public static partial class SqlServerErrors
{
    public const int UniqueIndexViolation = 2601;
    public const int UniqueConstraintViolation = 2627;
    public const int ConstraintConflict = 547;
    public const int Deadlock = 1205;
    public const int LockRequestTimeout = 1222;
    public const int RlsBlockPredicateConflict = 33504;

    public static bool IsUniqueViolationNumber(int number)
        => number is UniqueIndexViolation or UniqueConstraintViolation;

    public static bool IsUniqueViolation(Exception exception)
        => Find(exception) is { } sql && IsUniqueViolationNumber(sql.Number);

    /// <summary>Error 547 also covers CHECK constraints, so the message decides.</summary>
    public static bool IsForeignKeyViolation(Exception exception)
        => Find(exception) is { Number: ConstraintConflict } sql
            && sql.Message.Contains("FOREIGN KEY", StringComparison.Ordinal);

    public static bool IsDeadlock(Exception exception)
        => Find(exception) is { Number: Deadlock };

    public static bool IsLockTimeout(Exception exception)
        => Find(exception) is { Number: LockRequestTimeout };

    public static bool IsRlsBlock(Exception exception)
        => Find(exception) is { Number: RlsBlockPredicateConflict };

    public static string? ConstraintName(Exception exception)
        => Find(exception) is { } sql ? ParseConstraintName(sql.Message) : null;

    public static string? ParseConstraintName(string message)
    {
        var match = ConstraintNamePattern().Match(message);
        return match.Success ? match.Groups["name"].Value : null;
    }

    private static SqlException? Find(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql)
            {
                return sql;
            }
        }

        return null;
    }

    [GeneratedRegex("""(?:unique index|constraint) ['"](?<name>[^'"]+)['"]""")]
    private static partial Regex ConstraintNamePattern();
}
