namespace BemAqui.Commercial.Contracts;

/// <summary>
/// Contratos para os outros módulos (FIN-01 CA 20, ADR 0019 §4): o Finance libera o cancelamento do título de um contrato
/// cancelado. A implementação nasce com a COM-04a; até lá nenhum módulo a registra e o Finance trata como "não cancelado".
/// </summary>
public interface IContracts
{
    /// <summary>True se o contrato existe e está cancelado; null se não existe.</summary>
    Task<bool?> IsCancelledAsync(Guid contractId, CancellationToken cancellationToken = default);
}
