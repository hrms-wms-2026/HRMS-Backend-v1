using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Architecture rules for transactional email wired to platform_service_keys.
/// - The decrypted-key resolver may only be consumed inside Infrastructure, and
///   there only by the resolver implementation and the specific provider
///   adapters that are explicitly allowlisted below (the platform-key email
///   sender, and the Cloudflare R2 object storage adapter).
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

        // In Infrastructure only explicitly allowlisted provider adapters consume
        // it (the resolver implementation itself implements the interface
        // rather than consuming it).
        var allowed = new[]
        {
            "ONEVO.Infrastructure.ExternalServices.Email.PlatformKeyTransactionalEmailSender",
            "ONEVO.Infrastructure.ExternalServices.Storage.CloudflareR2.CloudflareR2ObjectStorageAdapter"
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

    [Fact]
    public void NoTenantControllerOrRequest_AcceptsOrWritesEmailProviderCredentials()
    {
        var nonSystemConfigControllers = ApiAssembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(ControllerBase)) &&
                        t.Namespace != null &&
                        !t.Namespace.Contains("SystemConfig"))
            .ToList();

        Assert.NotEmpty(nonSystemConfigControllers);

        foreach (var controller in nonSystemConfigControllers)
        {
            foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                foreach (var param in method.GetParameters())
                {
                    var paramTypeName = param.ParameterType.Name.ToLowerInvariant();
                    Assert.False(
                        paramTypeName.Contains("emailcredential") ||
                        paramTypeName.Contains("resendkey") ||
                        paramTypeName.Contains("sendgridkey"),
                        $"{controller.Name}.{method.Name} parameter '{param.Name}' of type '{param.ParameterType.Name}' must not accept tenant email provider credentials.");

                    foreach (var prop in param.ParameterType.GetProperties())
                    {
                        var propName = prop.Name.ToLowerInvariant();
                        Assert.False(
                            propName.Equals("resendapikey", StringComparison.OrdinalIgnoreCase) ||
                            propName.Equals("sendgridapikey", StringComparison.OrdinalIgnoreCase) ||
                            propName.Equals("smtppassword", StringComparison.OrdinalIgnoreCase) ||
                            propName.Equals("emailapikey", StringComparison.OrdinalIgnoreCase),
                            $"{param.ParameterType.Name}.{prop.Name} must not exist on a tenant request model.");
                    }
                }
            }
        }
    }

    [Fact]
    public void EmailOptions_HasNoProviderProperty()
    {
        var propertyNames = typeof(ONEVO.Infrastructure.ExternalServices.Email.EmailOptions)
            .GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain("Provider", propertyNames);
    }

    [Fact]
    public void PlatformKeyTransactionalEmailSender_HasNoConfigDrivenProviderSelectionOrSendgridFallback()
    {
        var source = File.ReadAllText(FindRepositoryPath(
            "src", "ONEVO.Infrastructure", "ExternalServices", "Email", "PlatformKeyTransactionalEmailSender.cs"));

        Assert.DoesNotContain("_options.Provider", source, StringComparison.Ordinal);
        Assert.DoesNotContain("options.Provider", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Email:Provider", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Email__Provider", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PlatformServiceKeyCatalog.Sendgrid", source, StringComparison.Ordinal);
        Assert.Contains("ResolveActiveTransactionalEmailProviderAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EmailAndServiceKeyFlow_DoesNotReferencePlatformOAuthApps()
    {
        var senderSource = File.ReadAllText(FindRepositoryPath(
            "src", "ONEVO.Infrastructure", "ExternalServices", "Email", "PlatformKeyTransactionalEmailSender.cs"));
        var resolverSource = File.ReadAllText(FindRepositoryPath(
            "src", "ONEVO.Infrastructure", "Services", "SystemConfig", "PlatformServiceKeyResolver.cs"));

        foreach (var source in new[] { senderSource, resolverSource })
        {
            Assert.DoesNotContain("PlatformOAuthApp", source, StringComparison.Ordinal);
            Assert.DoesNotContain("platform_oauth_apps", source, StringComparison.Ordinal);
        }

        var featureDirectory = FindRepositoryPath(
            "src", "ONEVO.Application", "Features", "DevPlatform", "SystemConfig", "PlatformServiceKeys");
        var offenders = Directory.GetFiles(featureDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return text.Contains("PlatformOAuthApp", StringComparison.Ordinal) ||
                       text.Contains("platform_oauth_apps", StringComparison.Ordinal);
            })
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(offenders);
    }

    private static string FindRepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "ONEVO.Api")))
            {
                return Path.Combine([directory.FullName, .. segments]);
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
