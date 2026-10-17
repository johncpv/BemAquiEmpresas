using BemAqui.SharedKernel;

namespace BemAqui.BuildingBlocks.Messaging;

/// <summary>
/// Reação a um evento de outro módulo. Roda na própria transação, junto com o registro na inbox:
/// o efeito acontece no máximo uma vez por handler e mensagem, mesmo com reentrega.
/// </summary>
public interface IIntegrationEventConsumer<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}

/// <summary>Publica no outbox do DbContext atual; grava junto com o SaveChanges do comando.</summary>
public interface IEventPublisher
{
    void Publish<TEvent>(TEvent integrationEvent, Guid? correlationId = null)
        where TEvent : IIntegrationEvent;
}
