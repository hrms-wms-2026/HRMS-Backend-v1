using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Api.Contracts.WorkManagement.Tasks;
using ONEVO.Api.Dashboard.Widgets;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Dashboard.Layout.Data;
using ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;
using ONEVO.Application.Features.WorkManagement.Tasks.Queries.GetMyActiveTasks;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Api.Dashboard.Widgets;

/// <summary>The real provider registrations driven by the real orchestrator (child scope per provider).</summary>
public sealed class WidgetDataPipelineTests
{
    [Fact]
    public async Task Registered_providers_load_through_the_orchestrator()
    {
        var kit = new WidgetProviderTestKit(permissions: [], activeModules: ["tasks"]);
        kit.Mediator.Setup(m => m.Send(It.IsAny<GetMyActiveTasksQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MyTasksResponse>.Success(new MyTasksResponse([])));

        var services = new ServiceCollection();
        services.AddScoped(_ => kit.Mediator.Object);
        services.AddScoped(_ => kit.User.Object);
        services.AddScoped(_ => kit.Entitlements.Object);
        services.AddScoped<TenantContextStub>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContextStub>());
        services.AddScoped<IWritableTenantContext>(sp => sp.GetRequiredService<TenantContextStub>());
        services.AddDashboardWidgetDataProviders();
        await using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        var requestTenant = new TenantContextStub();
        requestTenant.Resolve(new TenantRegistryEntry(WidgetProviderTestKit.TenantId, "acme", TenantStatus.Active, null));
        var orchestrator = new WidgetDataOrchestrator(
            root.GetRequiredService<IServiceScopeFactory>(), root.GetRequiredService<IServiceProviderIsKeyedService>(),
            requestTenant, new FakeDateTimeProvider(), NullLogger<WidgetDataOrchestrator>.Instance);

        var data = await orchestrator.LoadAsync(["my-tasks", "team-status", "todays-schedule"], default);

        Assert.Equal(WidgetDataStatus.Success, data["my-tasks"].State.Status);
        Assert.IsType<MyTasksViewModel>(data["my-tasks"].Data);
        // No attendance:read permission -> the provider's gate denies it, like the endpoint's 403.
        Assert.Equal((WidgetDataStatus.Error, WidgetDataErrors.Forbidden), (data["team-status"].State.Status, data["team-status"].State.Error));
        Assert.Equal(WidgetDataStatus.Client, data["todays-schedule"].State.Status);
    }

    private sealed class TenantContextStub : IWritableTenantContext
    {
        public Guid TenantId { get; private set; }
        public string? Slug { get; private set; }
        public TenantStatus? Status { get; private set; }
        public bool IsResolved { get; private set; }
        public TenantContextMode ContextMode { get; private set; }

        public void Resolve(TenantRegistryEntry tenant)
        {
            (TenantId, Slug, Status, IsResolved, ContextMode) = (tenant.TenantId, tenant.Slug, tenant.Status, true, TenantContextMode.Tenant);
        }

        public void SetAdminMode() => ContextMode = TenantContextMode.Admin;

        public void SetSystemMode() => ContextMode = TenantContextMode.System;
    }
}
