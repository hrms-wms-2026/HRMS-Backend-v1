using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace ONEVO.Tests.Architecture;

public class LayerDependencyTests
{
    private static readonly ArchUnitNET.Domain.Architecture Architecture = new ArchLoader()
        .LoadAssemblies(
            typeof(ONEVO.Domain.Common.BaseEntity).Assembly,
            typeof(ONEVO.Application.DependencyInjection).Assembly,
            typeof(ONEVO.Infrastructure.DependencyInjection).Assembly)
        .Build();

    private readonly IObjectProvider<IType> _domainLayer =
        Types().That().ResideInNamespace("ONEVO\\.Domain.*").As("Domain Layer");

    private readonly IObjectProvider<IType> _applicationLayer =
        Types().That().ResideInNamespace("ONEVO\\.Application.*").As("Application Layer");

    private readonly IObjectProvider<IType> _infrastructureLayer =
        Types().That().ResideInNamespace("ONEVO\\.Infrastructure.*").As("Infrastructure Layer");

    [Fact]
    public void Domain_ShouldNotDependOn_Application()
    {
        var rule = Types().That().Are(_domainLayer)
            .Should().NotDependOnAny(_applicationLayer)
            .WithoutRequiringPositiveResults();
        rule.Check(Architecture);
    }

    [Fact]
    public void Domain_ShouldNotDependOn_Infrastructure()
    {
        var rule = Types().That().Are(_domainLayer)
            .Should().NotDependOnAny(_infrastructureLayer)
            .WithoutRequiringPositiveResults();
        rule.Check(Architecture);
    }

    [Fact]
    public void Application_ShouldNotDependOn_Infrastructure()
    {
        var rule = Types().That().Are(_applicationLayer)
            .Should().NotDependOnAny(_infrastructureLayer)
            .WithoutRequiringPositiveResults();
        rule.Check(Architecture);
    }

    [Fact]
    public void Application_ShouldNotReference_EntityFrameworkCore()
    {
        var references = typeof(ONEVO.Application.DependencyInjection)
            .Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToList();

        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", references);
    }

    [Fact]
    public void ApplicationFeatureTypes_ShouldNotDependOn_DbContextTypes()
    {
        var forbiddenTypeNames = new[]
        {
            "IApplicationDbContext",
            "ApplicationDbContext",
            "DbSet"
        };

        var featureTypes = typeof(ONEVO.Application.DependencyInjection)
            .Assembly
            .GetTypes()
            .Where(type => type.Namespace?.StartsWith("ONEVO.Application.Features.", StringComparison.Ordinal) == true)
            .ToList();

        var offenders = featureTypes
            .SelectMany(type => GetDirectTypeReferences(type)
                .Where(reference => forbiddenTypeNames.Any(reference.Contains))
                .Select(reference => $"{type.FullName} -> {reference}"))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ApplicationFeatures_ShouldNotUse_FeatureLevelValidatorsFolder()
    {
        var offenders = typeof(ONEVO.Application.DependencyInjection)
            .Assembly
            .GetTypes()
            .Where(type => type.Namespace is not null
                && type.Namespace.StartsWith("ONEVO.Application.Features.", StringComparison.Ordinal)
                && (type.Namespace.EndsWith(".Validators", StringComparison.Ordinal)
                    || type.Namespace.Contains(".Validators.", StringComparison.Ordinal)))
            .Select(type => type.FullName)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void ApplicationFeatures_ShouldNotUse_FeatureLevelEventsFolder()
    {
        var offenders = typeof(ONEVO.Application.DependencyInjection)
            .Assembly
            .GetTypes()
            .Where(type => type.Namespace is not null
                && type.Namespace.StartsWith("ONEVO.Application.Features.", StringComparison.Ordinal)
                && (type.Namespace.EndsWith(".Events", StringComparison.Ordinal)
                    || type.Namespace.Contains(".Events.", StringComparison.Ordinal)))
            .Select(type => type.FullName)
            .ToList();

        Assert.Empty(offenders);
    }

    private static IEnumerable<string> GetDirectTypeReferences(System.Type type)
    {
        foreach (var field in type.GetFields(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic))
        {
            yield return field.FieldType.FullName ?? field.FieldType.Name;
        }

        foreach (var constructor in type.GetConstructors(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic))
        {
            foreach (var parameter in constructor.GetParameters())
                yield return parameter.ParameterType.FullName ?? parameter.ParameterType.Name;
        }

        foreach (var method in type.GetMethods(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.DeclaredOnly))
        {
            yield return method.ReturnType.FullName ?? method.ReturnType.Name;

            foreach (var parameter in method.GetParameters())
                yield return parameter.ParameterType.FullName ?? parameter.ParameterType.Name;
        }
    }
}
