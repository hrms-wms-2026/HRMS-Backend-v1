using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Inbox.Contracts;

namespace ONEVO.Application.Features.Inbox.Services;

public interface IInboxPersonCaseService
{
    Task<IReadOnlyList<InboxPersonCase>> GetPersonCasesAsync(
        Guid tenantId, Guid callerEmployeeId, Guid employeeId, string category, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    Task<InboxPersonCaseMonths> GetPersonCaseMonthsAsync(
        Guid tenantId, Guid callerEmployeeId, Guid employeeId, string category, int year, int offsetMinutes, CancellationToken ct);
}

/// <summary>
/// A person's case history for the ticket details panel: fans out to the sources that implement
/// <see cref="IInboxPersonCaseProvider"/>. Visibility of the person is enforced at the endpoint (RequireVisibleEmployee).
/// </summary>
public sealed class InboxPersonCaseService : IInboxPersonCaseService
{
    public const string Cancelled = "cancelled";

    private readonly IReadOnlyList<IInboxPersonCaseProvider> _providers;
    private readonly IEmployeeRepository _employees;

    public InboxPersonCaseService(IEnumerable<IInboxSourceProvider> providers, IEmployeeRepository employees)
    {
        _providers = providers.OfType<IInboxPersonCaseProvider>().ToList();
        _employees = employees;
    }

    public async Task<IReadOnlyList<InboxPersonCase>> GetPersonCasesAsync(
        Guid tenantId, Guid callerEmployeeId, Guid employeeId, string category, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var rows = new List<InboxPersonCase>();
        foreach (var provider in _providers)
            rows.AddRange(await provider.ListPersonCasesAsync(tenantId, employeeId, category, from, to, callerEmployeeId, ct));
        return rows.OrderByDescending(r => r.Item.CreatedAt).ToList();
    }

    public async Task<InboxPersonCaseMonths> GetPersonCaseMonthsAsync(
        Guid tenantId, Guid callerEmployeeId, Guid employeeId, string category, int year, int offsetMinutes, CancellationToken ct)
    {
        var offset = TimeSpan.FromMinutes(offsetMinutes);
        var from = new DateTimeOffset(year, 1, 1, 0, 0, 0, offset);
        var rows = await GetPersonCasesAsync(tenantId, callerEmployeeId, employeeId, category, from, from.AddYears(1), ct);
        var months = rows
            .Where(r => !(r.Item.Category == InboxCategories.Request && r.Item.Status == Cancelled))
            .GroupBy(r => r.Item.CreatedAt.ToOffset(offset).Month)
            .Select(g => new InboxPersonCaseMonth(g.Key, g.Count()))
            .OrderBy(m => m.Month)
            .ToList();
        var employee = await _employees.GetByIdAsync(tenantId, employeeId, ct);
        return new InboxPersonCaseMonths(employee?.HireDate, months);
    }
}
