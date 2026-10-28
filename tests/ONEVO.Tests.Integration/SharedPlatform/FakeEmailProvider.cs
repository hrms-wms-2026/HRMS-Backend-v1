using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.OutboxHandlers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.ServiceInterfaces;
using ONEVO.Infrastructure.ExternalServices.Email;

namespace ONEVO.Tests.Integration.SharedPlatform;

/// <summary>
/// Stands in for the email provider's HTTP API underneath the real email pipeline
/// (PasswordResetEmailOutboxHandler -> TransactionalEmailService -> PlatformKeyTransactionalEmailSender
/// -> ResendEmailAdapter / SendGridEmailAdapter), recording every request the provider "accepts".
/// </summary>
public sealed class FakeEmailProvider : HttpMessageHandler
{
    public sealed record AcceptedSend(string? IdempotencyKey, string Body);

    private readonly ConcurrentQueue<AcceptedSend> _accepted = new();
    private readonly string _provider;

    public FakeEmailProvider(string provider) => _provider = provider;

    public IReadOnlyList<AcceptedSend> Accepted => _accepted.ToList();

    /// <summary>When set, runs after a request is accepted and before the response is returned
    /// (e.g. to freeze the caller after the provider already took the email).</summary>
    public Func<int, Task>? AfterAccept { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var key = request.Headers.TryGetValues(ResendEmailAdapter.IdempotencyKeyHeader, out var values) ? values.Single() : null;
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(CancellationToken.None);
        _accepted.Enqueue(new AcceptedSend(key, body));

        if (AfterAccept is { } afterAccept)
            await afterAccept(_accepted.Count);

        return _provider == "sendgrid"
            ? new HttpResponseMessage(HttpStatusCode.Accepted)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{{\"id\":\"re-{Guid.NewGuid():N}\"}}") };
    }

    /// <summary>Registers the production email pipeline with this fake as the provider's HTTP API
    /// and the active platform key resolved to <c>provider</c> ("resend" or "sendgrid").</summary>
    public void Register(IServiceCollection services)
    {
        services.AddSingleton<IHttpClientFactory>(new Factory(this));
        services.AddSingleton<IPlatformServiceKeyResolver>(new Resolver(_provider));
        services.AddSingleton(Options.Create(new EmailOptions
        {
            FromAddress = "no-reply@onevo.test", FromName = "ONEVO", AppBaseUrl = "http://localhost:4200",
        }));
        services.AddScoped<IEmailTemplateRenderer, EmailTemplateRenderer>();
        services.AddScoped<IEmailProviderAdapter, SendGridEmailAdapter>();
        services.AddScoped<IEmailProviderAdapter, ResendEmailAdapter>();
        services.AddScoped<ITransactionalEmailSender, PlatformKeyTransactionalEmailSender>();
        services.AddScoped<IEmailService, TransactionalEmailService>();
        services.AddScoped<IOutboxMessageHandler, PasswordResetEmailOutboxHandler>();
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class Resolver(string provider) : IPlatformServiceKeyResolver
    {
        public Task<string?> ResolveActiveKeyAsync(string serviceKey, CancellationToken ct) => Task.FromResult<string?>(null);

        public Task<TransactionalEmailProviderResolution> ResolveActiveTransactionalEmailProviderAsync(CancellationToken ct)
            => Task.FromResult(TransactionalEmailProviderResolution.Resolved(provider, "fake-provider-key"));
    }
}
