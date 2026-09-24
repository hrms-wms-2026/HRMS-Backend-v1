using System.Text.Json;
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Queries.GetCurrentSession;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class GetCurrentSessionQueryHandlerTests
{
    private static Mock<ICurrentUser> MakeAuthenticatedCurrentUser(
        Guid userId, Guid tenantId, DateTimeOffset expires)
    {
        var current = new Mock<ICurrentUser>();
        current.SetupGet(instance => instance.IsAuthenticated).Returns(true);
        current.SetupGet(instance => instance.UserId).Returns(userId);
        current.SetupGet(instance => instance.TenantId).Returns(tenantId);
        current.SetupGet(instance => instance.Email).Returns("owner@acme.test");
        current.SetupGet(instance => instance.Permissions).Returns(new[] { "people.read" });
        current.SetupGet(instance => instance.SessionExpiresAt).Returns(expires);
        return current;
    }

    private static Mock<ITenantContext> MakeResolvedTenantContext(Guid tenantId, string slug = "acme")
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(instance => instance.IsResolved).Returns(true);
        tenant.SetupGet(instance => instance.ContextMode).Returns(TenantContextMode.Tenant);
        tenant.SetupGet(instance => instance.TenantId).Returns(tenantId);
        tenant.SetupGet(instance => instance.Slug).Returns(slug);
        return tenant;
    }

    private static Mock<IEmployeeRepository> MakeEmployees(
        Guid tenantId,
        Guid userId,
        Employee? employee)
    {
        var employees = new Mock<IEmployeeRepository>();
        employees
            .Setup(instance => instance.GetByUserIdAsync(
                tenantId,
                userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(employee);
        return employees;
    }

    [Fact]
    public async Task Me_ReturnsOnlyCurrentTenantSessionMetadata()
    {
        var tenantId = Guid.NewGuid();
        var expires = DateTimeOffset.UtcNow.AddMinutes(20);
        var userId = Guid.NewGuid();
        var current = MakeAuthenticatedCurrentUser(userId, tenantId, expires);
        var tenantContext = MakeResolvedTenantContext(tenantId);

        var entitlements = new Mock<IModuleEntitlementService>();
        entitlements
            .Setup(instance => instance.GetActiveModuleKeysForTenantAsync(
                tenantId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "people" });

        var tenants = new Mock<ITenantRepository>();
        tenants
            .Setup(instance => instance.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = tenantId, Slug = "acme", Name = "Acme" });

        var employees = MakeEmployees(tenantId, userId, employee: null);
        var handler = new GetCurrentSessionQueryHandler(
            current.Object,
            tenantContext.Object,
            entitlements.Object,
            tenants.Object,
            employees.Object);

        var result = await handler.Handle(new GetCurrentSessionQuery(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Authenticated.Should().BeTrue();
        result.Value.User!.UserId.Should().Be(userId);
        result.Value.User.TenantId.Should().Be(tenantId);
        result.Value.Permissions.Should().Equal("people.read");
        result.Value.ActiveModules.Should().Equal("people");
        result.Value.ExpiresAt.Should().Be(expires);
    }

    [Fact]
    public async Task Me_IncludesWorkspaceSlugAndDisplayName()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var current = MakeAuthenticatedCurrentUser(userId, tenantId, DateTimeOffset.UtcNow.AddMinutes(20));
        var tenantContext = MakeResolvedTenantContext(tenantId);

        var entitlements = new Mock<IModuleEntitlementService>();
        entitlements
            .Setup(instance => instance.GetActiveModuleKeysForTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        var tenants = new Mock<ITenantRepository>();
        tenants
            .Setup(instance => instance.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = tenantId, Slug = "acme", Name = "Acme Inc" });

        var employees = MakeEmployees(tenantId, userId, employee: null);
        var handler = new GetCurrentSessionQueryHandler(
            current.Object, tenantContext.Object, entitlements.Object, tenants.Object, employees.Object);

        var result = await handler.Handle(new GetCurrentSessionQuery(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Workspace.Should().NotBeNull();
        result.Value.Workspace!.Slug.Should().Be("acme");
        result.Value.Workspace.DisplayName.Should().Be("Acme Inc");
    }

    [Fact]
    public async Task Me_ReturnsUnauthenticatedFailure_WhenNotAuthenticated()
    {
        var current = new Mock<ICurrentUser>();
        current.SetupGet(instance => instance.IsAuthenticated).Returns(false);

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.SetupGet(instance => instance.IsResolved).Returns(false);

        var entitlements = new Mock<IModuleEntitlementService>();
        var tenants = new Mock<ITenantRepository>();
        var employees = new Mock<IEmployeeRepository>();

        var handler = new GetCurrentSessionQueryHandler(
            current.Object, tenantContext.Object, entitlements.Object, tenants.Object, employees.Object);

        var result = await handler.Handle(new GetCurrentSessionQuery(), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
        result.Error.Should().Be("Authentication required.");
        tenants.Verify(
            instance => instance.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Me_DoesNotSerializeTenantIdOrUserId()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var current = MakeAuthenticatedCurrentUser(userId, tenantId, DateTimeOffset.UtcNow.AddMinutes(20));
        var tenantContext = MakeResolvedTenantContext(tenantId);

        var entitlements = new Mock<IModuleEntitlementService>();
        entitlements
            .Setup(instance => instance.GetActiveModuleKeysForTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        var tenants = new Mock<ITenantRepository>();
        tenants
            .Setup(instance => instance.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = tenantId, Slug = "acme", Name = "Acme Inc" });

        var employees = MakeEmployees(tenantId, userId, employee: null);
        var handler = new GetCurrentSessionQueryHandler(
            current.Object, tenantContext.Object, entitlements.Object, tenants.Object, employees.Object);

        var result = await handler.Handle(new GetCurrentSessionQuery(), default);

        var json = JsonSerializer.Serialize(result.Value);

        json.Should().NotContain("tenant_id");
        json.Should().NotContain("tenantId");
        json.Should().NotContain("user_id");
        json.Should().NotContain("userId");
        json.Should().NotContain(tenantId.ToString());
        json.Should().NotContain(userId.ToString());
        json.Should().Contain("\"slug\":\"acme\"");
        json.Should().Contain("\"display_name\":\"Acme Inc\"");
    }

    [Fact]
    public async Task Handle_UserHasEmployeeRecord_IncludesEmployeeIdInResponse()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var current = MakeAuthenticatedCurrentUser(userId, tenantId, DateTimeOffset.UtcNow.AddMinutes(20));
        var tenantContext = MakeResolvedTenantContext(tenantId);

        var entitlements = new Mock<IModuleEntitlementService>();
        entitlements
            .Setup(instance => instance.GetActiveModuleKeysForTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        var tenants = new Mock<ITenantRepository>();
        tenants
            .Setup(instance => instance.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = tenantId, Slug = "acme", Name = "Acme" });

        var employees = MakeEmployees(tenantId, userId, new Employee
        {
            Id = employeeId,
            TenantId = tenantId,
            UserId = userId,
            FirstName = "Jane",
            LastName = "Doe",
            Email = "jane@example.com",
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
            EmployeeNumber = "E-001"
        });

        var handler = new GetCurrentSessionQueryHandler(
            current.Object, tenantContext.Object, entitlements.Object, tenants.Object, employees.Object);

        var result = await handler.Handle(new GetCurrentSessionQuery(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.User!.EmployeeId.Should().Be(employeeId);
    }

    [Fact]
    public async Task Handle_UserHasNoEmployeeRecord_EmployeeIdIsNull()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var current = MakeAuthenticatedCurrentUser(userId, tenantId, DateTimeOffset.UtcNow.AddMinutes(20));
        var tenantContext = MakeResolvedTenantContext(tenantId);

        var entitlements = new Mock<IModuleEntitlementService>();
        entitlements
            .Setup(instance => instance.GetActiveModuleKeysForTenantAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        var tenants = new Mock<ITenantRepository>();
        tenants
            .Setup(instance => instance.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = tenantId, Slug = "acme", Name = "Acme" });

        var employees = MakeEmployees(tenantId, userId, employee: null);

        var handler = new GetCurrentSessionQueryHandler(
            current.Object, tenantContext.Object, entitlements.Object, tenants.Object, employees.Object);

        var result = await handler.Handle(new GetCurrentSessionQuery(), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.User!.EmployeeId.Should().BeNull();
    }
}
