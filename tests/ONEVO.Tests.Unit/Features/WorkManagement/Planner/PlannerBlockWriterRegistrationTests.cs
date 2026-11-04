using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application;
using ONEVO.Application.Features.WorkManagement.Planner.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Appliers;
using ONEVO.Application.Features.WorkManagement.Tasks.Reverters;
using ONEVO.Infrastructure;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Planner;

/// <summary>
/// The task.create applier and reverter take their planner and assignment services as optional constructor parameters, so a
/// missing registration would not fail: an approved work item request would quietly skip the assign and the plan. Check the
/// real registrations (Planner create popover, 2026-11-03).
/// </summary>
public class PlannerBlockWriterRegistrationTests
{
    private static readonly IServiceCollection Services = Build();

    private static IServiceCollection Build()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=di_registration_test_only"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        return services;
    }

    [Fact]
    public void PlannerBlockWriter_IsRegistered_AsThePlannerService()
    {
        Services.Should().Contain(d => d.ServiceType == typeof(IPlannerBlockWriter) && d.ImplementationFactory != null);
        Services.Should().Contain(d => d.ServiceType == typeof(PlannerService));
    }

    [Theory]
    [InlineData(typeof(TaskCreateApplier))]
    [InlineData(typeof(TaskCreateReverter))]
    public void EveryConstructorDependency_IncludingTheOptionalOnes_IsRegistered(Type type)
    {
        foreach (var parameter in type.GetConstructors().Single().GetParameters())
            Services.Should().Contain(d => d.ServiceType == parameter.ParameterType, $"{type.Name} needs {parameter.ParameterType.Name}");
    }
}
