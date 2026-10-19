using System.Text.Json;
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Queries.GetInboxPersonCases;
using ONEVO.Application.Features.Inbox.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Inbox;

public class InboxPersonCaseServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Subject = Guid.NewGuid();
    private static readonly DateTimeOffset From = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly FakePersonCaseProvider _monitoring = new("monitoring");
    private readonly FakePersonCaseProvider _leave = new("leave");
    private readonly PlainProvider _work = new("work");
    private readonly Mock<IEmployeeRepository> _employees = new();

    private InboxPersonCaseService Service() =>
        new(new IInboxSourceProvider[] { _monitoring, _work, _leave }, _employees.Object);

    private static InboxPersonCase Case(string source, DateTimeOffset createdAt, string status = "open", bool canOpen = true, string category = InboxCategories.Alert)
        => new(new InboxItem(source, "case", Guid.NewGuid(), category, "exception", $"{source} {createdAt:MM-dd}", null,
            status, InboxDirections.Info, null, "employee", Subject, null, new InboxPerson(Subject, "Bala"), null,
            createdAt, createdAt, [], 0, false, new Dictionary<string, IReadOnlyList<string>>()), canOpen);

    [Fact]
    public async Task GetPersonCasesAsync_MergesProvidersNewestFirst()
    {
        _monitoring.Cases.AddRange([Case("monitoring", From.AddDays(4)), Case("monitoring", From.AddDays(11))]);
        _leave.Cases.Add(Case("leave", From.AddDays(19)));

        var rows = await Service().GetPersonCasesAsync(TenantId, Me, Subject, InboxCategories.Alert, From, To, default);

        rows.Select(r => r.Item.CreatedAt.Day).Should().Equal(20, 12, 5);
    }

    [Fact]
    public async Task GetPersonCasesAsync_PassesCategoryAndRangeThrough()
    {
        await Service().GetPersonCasesAsync(TenantId, Me, Subject, InboxCategories.Alert, From, To, default);

        _monitoring.Calls.Should().ContainSingle().Which.Should().Be((Subject, InboxCategories.Alert, From, To, Me));
        _leave.Calls.Should().ContainSingle().Which.Should().Be((Subject, InboxCategories.Alert, From, To, Me));
    }

    [Fact]
    public async Task GetPersonCaseMonthsAsync_GroupsByLocalMonth()
    {
        _monitoring.Cases.AddRange([
            Case("monitoring", new DateTimeOffset(2026, 10, 31, 19, 0, 0, TimeSpan.Zero)),
            Case("monitoring", new DateTimeOffset(2026, 10, 31, 17, 0, 0, TimeSpan.Zero))]);

        var months = await Service().GetPersonCaseMonthsAsync(TenantId, Me, Subject, InboxCategories.Alert, 2026, 330, default);

        months.Months.Should().Equal(new InboxPersonCaseMonth(10, 1), new InboxPersonCaseMonth(11, 1));
        var (from, to) = (_monitoring.Calls[0].From, _monitoring.Calls[0].To);
        from.Should().Be(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromMinutes(330)));
        to.Should().Be(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.FromMinutes(330)));
    }

    [Fact]
    public async Task GetPersonCaseMonthsAsync_DropsCancelled()
    {
        _leave.Cases.AddRange([
            Case("leave", new DateTimeOffset(2026, 3, 5, 9, 0, 0, TimeSpan.Zero), status: "cancelled", category: InboxCategories.Request),
            Case("leave", new DateTimeOffset(2026, 4, 5, 9, 0, 0, TimeSpan.Zero), status: "approved", category: InboxCategories.Request)]);

        var months = await Service().GetPersonCaseMonthsAsync(TenantId, Me, Subject, InboxCategories.Request, 2026, 0, default);

        months.Months.Should().Equal(new InboxPersonCaseMonth(4, 1));
    }

    [Fact]
    public async Task GetPersonCaseMonthsAsync_ReturnsHireDate()
    {
        _employees.Setup(e => e.GetByIdAsync(TenantId, Subject, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { HireDate = new DateOnly(2025, 6, 15) });

        var months = await Service().GetPersonCaseMonthsAsync(TenantId, Me, Subject, InboxCategories.Alert, 2026, 0, default);

        months.HireDate.Should().Be(new DateOnly(2025, 6, 15));
    }

    [Theory]
    [InlineData("other")]
    [InlineData("")]
    public void Query_RejectsUnknownCategory(string category)
        => GetInboxPersonCasesQuery.Validate(category, From, To).Should().NotBeNull();

    [Fact]
    public void Query_RejectsRangeOver31Days()
        => GetInboxPersonCasesQuery.Validate(InboxCategories.Alert, From, From.AddDays(32)).Should().NotBeNull();

    [Fact]
    public void Query_RejectsEmptyRange()
        => GetInboxPersonCasesQuery.Validate(InboxCategories.Alert, From, From).Should().NotBeNull();

    [Fact]
    public void Query_AcceptsOneMonth()
        => GetInboxPersonCasesQuery.Validate(InboxCategories.Request, From, To).Should().BeNull();

    private class PlainProvider(string source) : IInboxSourceProvider
    {
        public string Source => source;
        public string Label => source;
        public Task<IReadOnlyList<InboxItem>> ListAsync(Guid tenantId, InboxQuery query, Guid callerEmployeeId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<InboxItem>>([]);
        public Task<InboxItemDetail?> GetDetailAsync(Guid tenantId, string kind, Guid id, Guid callerEmployeeId, CancellationToken ct)
            => Task.FromResult<InboxItemDetail?>(null);
        public Task<IReadOnlyList<InboxFilterField>> GetFilterFieldsAsync(Guid tenantId, Guid callerEmployeeId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<InboxFilterField>>([]);
        public Task<Result<InboxActionResult>> ExecuteActionAsync(
            Guid tenantId, string kind, Guid id, string action, JsonElement? body, Guid callerEmployeeId, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class FakePersonCaseProvider(string source) : PlainProvider(source), IInboxPersonCaseProvider
    {
        public List<InboxPersonCase> Cases { get; } = [];
        public List<(Guid Subject, string Category, DateTimeOffset From, DateTimeOffset To, Guid Caller)> Calls { get; } = [];

        public Task<IReadOnlyList<InboxPersonCase>> ListPersonCasesAsync(
            Guid tenantId, Guid subjectEmployeeId, string category, DateTimeOffset from, DateTimeOffset to,
            Guid callerEmployeeId, CancellationToken ct)
        {
            Calls.Add((subjectEmployeeId, category, from, to, callerEmployeeId));
            return Task.FromResult<IReadOnlyList<InboxPersonCase>>(
                Cases.Where(c => c.Item.CreatedAt >= from && c.Item.CreatedAt < to).ToList());
        }
    }
}
