namespace ONEVO.Application.Common.Exceptions;

/// <summary>
/// Application-layer signal for a database unique-constraint violation (e.g. two concurrent
/// requests racing on the same email/employee-number/token), thrown by repository
/// implementations instead of leaking a provider-specific exception type (e.g. EF Core's
/// DbUpdateException wrapping Npgsql's PostgresException) across the Application/Infrastructure
/// boundary.
/// </summary>
public class UniqueConstraintConflictException : Exception
{
    public UniqueConstraintConflictException(Exception innerException)
        : base("The request conflicts with an existing record.", innerException)
    {
    }
}
