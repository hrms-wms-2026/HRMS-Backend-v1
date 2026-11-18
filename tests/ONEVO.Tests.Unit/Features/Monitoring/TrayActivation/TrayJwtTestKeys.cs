using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using ONEVO.Api.Auth;
using ONEVO.Infrastructure.Services.Monitoring.TrayActivation;

namespace ONEVO.Tests.Unit.Features.Monitoring.TrayActivation;

/// <summary>Test helpers for SEC-15 tray device-token signing keys. Every key is generated per run.</summary>
internal static class TrayJwtTestKeys
{
    public static string NewPem(ECCurve? curve = null)
    {
        using var ec = ECDsa.Create(curve ?? ECCurve.NamedCurves.nistP256);
        return ec.ExportPkcs8PrivateKeyPem();
    }

    public static string NewLegacySecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    /// <summary>Config with the given (kid, pem) keys; <paramref name="activeKid"/> defaults to the first kid.</summary>
    public static Dictionary<string, string?> Settings(
        IReadOnlyList<(string Kid, string Pem)> keys, string? activeKid = null)
    {
        var settings = new Dictionary<string, string?>();
        for (var i = 0; i < keys.Count; i++)
        {
            settings[$"TrayApp:Jwt:SigningKeys:{i}:Kid"] = keys[i].Kid;
            settings[$"TrayApp:Jwt:SigningKeys:{i}:PrivateKeyPem"] = keys[i].Pem;
        }

        settings["TrayApp:Jwt:ActiveKid"] = activeKid ?? (keys.Count > 0 ? keys[0].Kid : null);
        return settings;
    }

    public static IConfiguration Config(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    /// <summary>
    /// A container with the real tray signing registration plus the API's authentication setup,
    /// as the running app wires them.
    /// </summary>
    public static ServiceProvider BuildServices(
        IConfiguration configuration, string environmentName = "Production", TimeProvider? time = null)
    {
        var webEnvironment = new Mock<IWebHostEnvironment>();
        webEnvironment.SetupGet(value => value.EnvironmentName).Returns(environmentName);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddSingleton<IHostEnvironment>(webEnvironment.Object);
        services.AddSingleton(time ?? TimeProvider.System);
        services.AddTrayJwtSigning();

        var extensions = typeof(AuthCookieNames).Assembly
            .GetType("ONEVO.Api.Extensions.AuthenticationExtensions", throwOnError: true)!;
        extensions
            .GetMethod("AddApiAuthentication",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public)!
            .Invoke(null, [services, webEnvironment.Object, configuration]);

        return services.BuildServiceProvider();
    }
}

internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
