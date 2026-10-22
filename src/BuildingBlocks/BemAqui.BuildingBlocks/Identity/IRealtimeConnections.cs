namespace BemAqui.BuildingBlocks.Identity;

/// <summary>
/// Conexões de tempo real abertas (hubs do SignalR) por usuário. Quando a permissão efetiva (<c>perm_version</c>) ou o security
/// stamp mudam, as conexões do usuário são fechadas depois do commit e a reconexão passa de novo pela autorização (PRD-02 CA 17,
/// ADR 0026 §4). Implementado pelo Host; fora dele, não faz nada.
/// </summary>
public interface IRealtimeConnections
{
    void CloseFor(Guid tenantId, IReadOnlyCollection<Guid> userIds);
}

/// <summary>Sem hub no processo (CLI, jobs isolados): nada a fechar.</summary>
public sealed class NoRealtimeConnections : IRealtimeConnections
{
    public void CloseFor(Guid tenantId, IReadOnlyCollection<Guid> userIds)
    {
    }
}
