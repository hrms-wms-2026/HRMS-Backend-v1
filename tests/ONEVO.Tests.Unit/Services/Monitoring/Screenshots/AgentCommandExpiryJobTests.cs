using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Services.Monitoring.Screenshots;
using Xunit;

namespace ONEVO.Tests.Unit.Services.Monitoring.Screenshots;

/// <summary>
/// Regression coverage for the same background-scope-defaults-to-System bug fixed in
/// ActivityDailySummaryJob/LocationRuleEvaluatorJob: agent_commands is FORCE RLS with an
/// admin-or-matching-tenant policy, but ExpireStaleCommandsAsync's bulk cross-tenant UPDATE ran
/// with no admin context established, so it silently matched zero rows every tick.
/// </summary>
public class AgentCommandExpiryJobTests
{
    [Fact]
    public async Task RunOnceAsync_CallsExpireStaleCommandsWhileInAdminMode()
    {
        var writableContext = new TenantContextAccessor();
        TenantContextMode? modeAtCall = null;

        var repo = new Mock<IAgentCommandRepository>();
        repo.Setup(r => r.ExpireStaleCommandsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                modeAtCall = writableContext.ContextMode;
                return 3;
            });

        var services = new ServiceCollection();
        services.AddSingleton(repo.Object);
        services.AddSingleton<IWritableTenantContext>(writableContext);

        var job = new AgentCommandExpiryJob(services.BuildServiceProvider(), NullLogger<AgentCommandExpiryJob>.Instance);
        await job.RunOnceAsync(CancellationToken.None);

        modeAtCall.Should().Be(TenantContextMode.Admin);
        repo.Verify(r => r.ExpireStaleCommandsAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
