using Microsoft.Extensions.DependencyInjection;

namespace ONEVO.Application.Features.Inbox.Contracts;

/// <summary>What an item kind can do. The frontend renders actions, dialogs and bulk selection from these.</summary>
[Flags]
public enum InboxCapabilities
{
    None = 0,
    Chat = 1,
    EditBeforeApprove = 2,
    Revert = 4,
    Escalate = 16,
    Resolve = 32,
    Cancel = 64,
    SubjectParticipates = 512,
}

/// <summary>One (source, kind) the inbox knows about. <see cref="Category"/> is <see cref="InboxCategories"/>;
/// <see cref="DetailRenderer"/> is the frontend renderer key (null = the default explanation + change-set view).</summary>
public sealed record InboxKindDescriptor(
    string Source, string Kind, string Label, string Category, InboxCapabilities Capabilities, string? DetailRenderer = null)
{
    public bool Has(InboxCapabilities capability) => (Capabilities & capability) == capability;
}

public interface IInboxKindRegistry
{
    IReadOnlyList<InboxKindDescriptor> All { get; }
    InboxKindDescriptor? Find(string source, string kind);
}

/// <summary>Built from every <see cref="InboxKindDescriptor"/> registered with <see cref="InboxKindServiceCollectionExtensions.AddInboxKind"/>.
/// Two registrations of the same (source, kind) are a wiring bug and fail on first resolve.</summary>
public sealed class InboxKindRegistry : IInboxKindRegistry
{
    private readonly Dictionary<(string, string), InboxKindDescriptor> _byKey = new();

    public InboxKindRegistry(IEnumerable<InboxKindDescriptor> descriptors)
    {
        foreach (var descriptor in descriptors)
        {
            if (!_byKey.TryAdd((descriptor.Source, descriptor.Kind), descriptor))
                throw new InvalidOperationException(
                    $"Inbox kind '{descriptor.Source}/{descriptor.Kind}' is registered more than once.");
        }
        All = _byKey.Values.OrderBy(d => d.Source).ThenBy(d => d.Kind).ToList();
    }

    public IReadOnlyList<InboxKindDescriptor> All { get; }

    public InboxKindDescriptor? Find(string source, string kind) => _byKey.GetValueOrDefault((source, kind));
}

public static class InboxKindServiceCollectionExtensions
{
    public static IServiceCollection AddInboxKind(this IServiceCollection services, InboxKindDescriptor descriptor)
        => services.AddSingleton(descriptor);
}
