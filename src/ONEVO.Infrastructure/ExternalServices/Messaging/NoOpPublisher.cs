using MediatR;

namespace ONEVO.Infrastructure.ExternalServices.Messaging;

public class NoOpPublisher : IPublisher
{
    public Task Publish(object notification, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
        => Task.CompletedTask;
}
