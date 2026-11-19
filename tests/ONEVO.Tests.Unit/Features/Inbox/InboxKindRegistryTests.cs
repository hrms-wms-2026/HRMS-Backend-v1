using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ONEVO.Application.Features.Inbox.Contracts;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Inbox;

public class InboxKindRegistryTests
{
    private static InboxKindDescriptor Leave(InboxCapabilities caps = InboxCapabilities.Chat)
        => new("leave", "request", "Leave request", InboxCategories.Request, caps, "leave-request");

    [Fact]
    public void Registering_the_same_kind_twice_fails_when_the_registry_is_built()
    {
        var services = new ServiceCollection().AddInboxKind(Leave()).AddInboxKind(Leave(InboxCapabilities.Escalate));
        services.AddSingleton<IInboxKindRegistry, InboxKindRegistry>();
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IInboxKindRegistry>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*leave/request*registered more than once*");
    }

    [Fact]
    public void Finds_descriptors_by_source_and_kind_and_lists_them_sorted()
    {
        var registry = new InboxKindRegistry([
            Leave(InboxCapabilities.Resolve | InboxCapabilities.Revert),
            new("attendance", "correction", "Correction", InboxCategories.Request, InboxCapabilities.Chat)]);

        registry.Find("leave", "request")!.Has(InboxCapabilities.Resolve).Should().BeTrue();
        registry.Find("leave", "request")!.Has(InboxCapabilities.Escalate).Should().BeFalse();
        registry.Find("leave", "nope").Should().BeNull();
        registry.All.Select(d => d.Source).Should().Equal("attendance", "leave");
    }
}
