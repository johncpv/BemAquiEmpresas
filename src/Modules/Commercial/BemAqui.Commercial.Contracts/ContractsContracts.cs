namespace BemAqui.Commercial.Contracts;

/// <summary>
/// Contratos para os outros módulos (FIN-01 CA 20, ADR 0019 §4): o Finance libera o cancelamento do título de um contrato
/// cancelado. Implementado pelo Commercial desde a COM-04a.
/// </summary>
public interface IContracts
{
    /// <summary>True se o contrato existe e está cancelado; null se não existe.</summary>
    Task<bool?> IsCancelledAsync(Guid contractId, CancellationToken cancellationToken = default);

    /// <summary>Número, situação e projeto do contrato (COM-04a CA 21); null se não existe.</summary>
    Task<ContractInfo?> FindAsync(Guid contractId, CancellationToken cancellationToken = default);
}

public sealed record ContractInfo(Guid Id, string Number, ContractState Status, Guid PartyId, Guid? ProjectId);
