using System.Net;
using FluentAssertions;
using ONEVO.Tests.Integration.Security.TenantAndObjectAccess;
using ONEVO.Tests.Integration.Support;
using Xunit;
using static ONEVO.Tests.Integration.Security.TenantAndObjectAccess.TenantAndObjectAccessFixture;

namespace ONEVO.Tests.Integration.DevPlatform;

/// <summary>
/// DEC-114: a tenant raises a ticket with the references (correlation ids) of recent failed requests, and the
/// ticket detail returns them, through the real API, migration and SQL Server column.
/// </summary>
[Collection(WebApplicationFactoryCollection.Name)]
public sealed class SupportTicketCorrelationIdsIntegrationTests : IClassFixture<TenantAndObjectAccessFixture>
{
    private readonly TenantAndObjectAccessFixture _f;

    public SupportTicketCorrelationIdsIntegrationTests(TenantAndObjectAccessFixture fixture) => _f = fixture;

    [Fact]
    public async Task CreateWithCorrelationIds_DetailReturnsThemAsList()
    {
        var first = Guid.NewGuid().ToString();
        var create = await _f.SendJsonAsync(_f.OwnerA, HttpMethod.Post, "/api/v1/support/tickets", new
        {
            subject = "Export fails",
            description = "Payroll export returns an error",
            priority = "high",
            category = "payroll",
            correlation_ids = new[] { first, "0HN7ABC-00000001", first },
        });
        create.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created], await create.Content.ReadAsStringAsync());
        var ticketId = (await ReadJsonAsync(create)).GetProperty("id").GetGuid();

        var detail = await _f.SendAsync(_f.OwnerA, HttpMethod.Get, $"/api/v1/support/tickets/{ticketId}");
        detail.StatusCode.Should().Be(HttpStatusCode.OK, await detail.Content.ReadAsStringAsync());

        var ids = (await ReadJsonAsync(detail)).GetProperty("correlation_ids")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        ids.Should().Equal(first, "0HN7ABC-00000001");
    }

    [Fact]
    public async Task CreateWithDisallowedCorrelationId_IsRejected()
    {
        var create = await _f.SendJsonAsync(_f.OwnerA, HttpMethod.Post, "/api/v1/support/tickets", new
        {
            subject = "Export fails",
            description = "Payroll export returns an error",
            priority = "high",
            correlation_ids = new[] { "not allowed; drop table" },
        });

        create.StatusCode.Should().Be(HttpStatusCode.BadRequest, await create.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TicketWithoutCorrelationIds_DetailReturnsEmptyList()
    {
        var detail = await _f.SendAsync(_f.OwnerA, HttpMethod.Get, $"/api/v1/support/tickets/{_f.A.SupportTicketId}");
        detail.StatusCode.Should().Be(HttpStatusCode.OK, await detail.Content.ReadAsStringAsync());

        (await ReadJsonAsync(detail)).GetProperty("correlation_ids").GetArrayLength().Should().Be(0);
    }
}
