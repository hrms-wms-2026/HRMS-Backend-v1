using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.Commands.UpdateTenant;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Tenancy;

public class UpdateTenantCommandHandlerAuditTests
{
    [Fact]
    public async Task Handle_RecordsPreviousValuesBeforeChangingThem()
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Old Co", Slug = "old-co", IndustryProfile = "retail" };
        var tenants = new Mock<ITenantRepository>();
        tenants.Setup(t => t.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        tenants.Setup(t => t.SlugExistsAsync("new-co", tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(c => c.IsAuthenticated).Returns(true);
        var auditTrail = new RecordingAuditTrail();
        var handler = new UpdateTenantCommandHandler(
            tenants.Object, currentUser.Object, new Mock<IUnitOfWork>().Object, new FakeDateTimeProvider(), auditTrail);

        var result = await handler.Handle(new UpdateTenantCommand(tenant.Id, "New Co", "new-co", "tech"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        tenant.Name.Should().Be("New Co");
        var before = auditTrail.BeforeJson();
        before.GetProperty("name").GetString().Should().Be("Old Co");
        before.GetProperty("slug").GetString().Should().Be("old-co");
        before.GetProperty("industryProfile").GetString().Should().Be("retail");
    }
}
