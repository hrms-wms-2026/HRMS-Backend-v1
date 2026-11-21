namespace ONEVO.Application.Common.ServiceInterfaces;

/// <summary>
/// SQL commands run through EF Core in the current scope (one HTTP request), for the request log line.
/// </summary>
public interface IRequestSqlMetrics
{
    int CommandCount { get; }
    TimeSpan TotalElapsed { get; }
}
