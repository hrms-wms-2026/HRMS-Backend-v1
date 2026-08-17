namespace ONEVO.Application.Common.Exceptions;

/// <summary>
/// Application-layer signal for a stale optimistic-concurrency write, thrown by repository
/// implementations instead of leaking a provider-specific exception type (e.g. EF Core's
/// DbUpdateConcurrencyException) across the Application/Infrastructure boundary.
/// </summary>
public class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException()
        : base("The record was modified by another request.")
    {
    }

    public ConcurrencyConflictException(Exception innerException)
        : base("The record was modified by another request.", innerException)
    {
    }
}
