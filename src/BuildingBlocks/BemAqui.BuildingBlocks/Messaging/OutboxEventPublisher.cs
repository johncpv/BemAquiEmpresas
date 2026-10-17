using System.Text.Json;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.SharedKernel;

namespace BemAqui.BuildingBlocks.Messaging;

/// <summary>Adiciona o evento ao outbox do DbContext do escopo; ele é gravado no SaveChanges do comando.</summary>
public sealed class OutboxEventPublisher(BemAquiDbContext db, TimeProvider clock, OutboxSignal signal) : IEventPublisher
{
    public void Publish<TEvent>(TEvent integrationEvent, Guid? correlationId = null)
        where TEvent : IIntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var now = clock.GetUtcNow();
        db.OutboxMessages.Add(new OutboxMessage
        {
            Type = IntegrationEventRegistry.NameOf(integrationEvent.GetType()),
            Payload = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType()),
            OccurredAt = now,
            NextAttemptAt = now,
            CorrelationId = correlationId,
        });
        signal.Notify();
    }
}

/// <summary>Acorda o dispatcher sem esperar o intervalo de polling.</summary>
public sealed class OutboxSignal : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(0, 1);

    public void Notify()
    {
        if (_semaphore.CurrentCount == 0)
        {
            try
            {
                _semaphore.Release();
            }
            catch (SemaphoreFullException)
            {
                // já sinalizado
            }
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _semaphore.WaitAsync(timeout, cancellationToken);

    public void Dispose() => _semaphore.Dispose();
}
