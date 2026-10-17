using System.Diagnostics.CodeAnalysis;
using BemAqui.SharedKernel;

namespace BemAqui.BuildingBlocks.Messaging;

/// <summary>Um handler registrado: nome estável (vai para a inbox), tipo e como invocá-lo.</summary>
public sealed record RegisteredHandler(string Name, Type HandlerType, Func<object, object, CancellationToken, Task> Invoke);

public sealed record RegisteredEvent(string Name, Type EventType, IReadOnlyList<RegisteredHandler> Handlers);

/// <summary>
/// Eventos e handlers conhecidos. O nome do evento (gravado no outbox) é o nome completo do tipo;
/// o nome do handler (gravado na inbox) também. Renomear um deles exige migração dos dados pendentes.
/// </summary>
public sealed class IntegrationEventRegistry
{
    private readonly Dictionary<string, (Type EventType, List<RegisteredHandler> Handlers)> _events = new(StringComparer.Ordinal);

    public static string NameOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.FullName ?? type.Name;
    }

    public IntegrationEventRegistry AddEvent<TEvent>()
        where TEvent : IIntegrationEvent
    {
        _events.TryAdd(NameOf(typeof(TEvent)), (typeof(TEvent), []));
        return this;
    }

    public IntegrationEventRegistry AddHandler<TEvent, THandler>()
        where TEvent : IIntegrationEvent
        where THandler : IIntegrationEventConsumer<TEvent>
    {
        AddEvent<TEvent>();
        var handlers = _events[NameOf(typeof(TEvent))].Handlers;
        if (handlers.All(h => h.HandlerType != typeof(THandler)))
        {
            handlers.Add(new RegisteredHandler(
                NameOf(typeof(THandler)),
                typeof(THandler),
                (handler, evt, ct) => ((THandler)handler).HandleAsync((TEvent)evt, ct)));
        }

        return this;
    }

    public bool TryGet(string name, [NotNullWhen(true)] out RegisteredEvent? registered)
    {
        if (_events.TryGetValue(name, out var entry))
        {
            registered = new RegisteredEvent(name, entry.EventType, entry.Handlers);
            return true;
        }

        registered = null;
        return false;
    }

    public IEnumerable<Type> HandlerTypes => _events.Values.SelectMany(e => e.Handlers).Select(h => h.HandlerType).Distinct();
}
