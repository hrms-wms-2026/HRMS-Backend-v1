using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Architecture rules for transactional email wired to platform_service_keys.
/// - The decrypted-key resolver may only be consumed inside Infrastructure, and there
///   only by the resolver implementation and the platform-key email sender.
/// - Application email code (outbox handlers, contracts) stays EF-free.
/// - No controller and no Application type may depend on the resolver.
/// </summary>
public class EmailPlatformKeyArchitectureTests
{
    private static readonly Assembly ApplicationAssembly =
        typeof(ONEVO.Application.Common.Models.Result).Assembly;

    private static readonly Assembly InfrastructureAssembly =
        typeof(ONEVO.Infrastructure.ExternalServices.Email.PlatformKeyTransactionalEmailSender).Assembly;

    private static readonly Assembly ApiAssembly =
        typeof(ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig.PlatformServiceKeysController).Assembly;

    private static IEnumerable<Type> TypesConsumingResolver(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsInterface)
                continue;
            var consumes = type.GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Any(p => p.ParameterType.Name == "IPlatformServiceKeyResolver");
            if (consumes)
                yield return type;
        }
    }

    [Fact]
    public void OnlyInfrastructureEmailSender_ConsumesPlatformServiceKeyResolver()
    {
        // Application declares the interface but no Application type may consume it.
        Assert.Empty(TypesConsumingResolver(ApplicationAssembly).Select(t => t.FullName));

        // No API type (controller or otherwise) may consume it.
        Assert.Empty(TypesConsumingResolver(ApiAssembly).Select(t => t.FullName));

        // In Infrastructure only the platform-key email sender consumes it (the resolver
        // implementation itself implements the interface rather than consuming it).
        var allowed = new[]
        {
            "ONEVO.Infrastructure.ExternalServices.Email.PlatformKeyTransactionalEmailSender"
        };
        var offenders = TypesConsumingResolver(InfrastructureAssembly)
            .Select(t => t.FullName)
            .Where(name => !allowed.Contains(name))
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void EmailSendingApplicationCode_DoesNotReference_EfCoreOrDbContext()
    {
        var emailTypes = new[]
        {
            typeof(ONEVO.Application.Common.ServiceInterfaces.ITransactionalEmailSender),
            typeof(ONEVO.Application.Common.ServiceInterfaces.IEmailService),
            typeof(ONEVO.Application.Features.DevPlatform.Provisioning.OutboxHandlers.TenantOwnerInviteEmailOutboxHandler),
            typeof(ONEVO.Application.Features.Auth.Login.Commands.RequestPasswordReset.RequestPasswordResetCommandHandler)
        };

        foreach (var type in emailTypes)
        {
            foreach (var ctor in type.GetConstructors())
            {
                foreach (var param in ctor.GetParameters())
                {
                    var ns = param.ParameterType.Namespace ?? string.Empty;
                    Assert.False(ns.StartsWith("Microsoft.EntityFrameworkCore"),
                        $"{type.FullName} takes an EF Core dependency: {param.ParameterType.FullName}");
                    Assert.NotEqual("ApplicationDbContext", param.ParameterType.Name);
                }
            }
        }
    }

    [Fact]
    public void NoController_DependsOnTransactionalEmailSenderOrResolver()
    {
        var controllerTypes = ApiAssembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(ControllerBase)))
            .ToList();
        Assert.NotEmpty(controllerTypes);

        foreach (var controller in controllerTypes)
        {
            foreach (var ctor in controller.GetConstructors())
            {
                var offender = ctor.GetParameters().FirstOrDefault(p =>
                    p.ParameterType.Name == "IPlatformServiceKeyResolver" ||
                    p.ParameterType.Name == "ITransactionalEmailSender");
                Assert.True(offender is null,
                    $"{controller.FullName} must not depend on {offender?.ParameterType.Name}.");
            }
        }
    }

    [Fact]
    public void TransactionalEmailContracts_HaveNoSecretShapedMembers()
    {
        var contractTypes = new[]
        {
            typeof(ONEVO.Application.Common.ServiceInterfaces.TransactionalEmailRequest),
            typeof(ONEVO.Application.Common.ServiceInterfaces.TransactionalEmailResult)
        };

        foreach (var type in contractTypes)
        {
            foreach (var property in type.GetProperties())
            {
                var name = property.Name.ToLowerInvariant();
                Assert.False(
                    name.Contains("apikey") || name.Contains("secret") ||
                    name.Contains("password") || name.Contains("encrypted"),
                    $"{type.Name}.{property.Name} must not exist on a no-secret contract.");
            }
        }
    }
}
