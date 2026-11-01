namespace BemAqui.BuildingBlocks.Persistence.Auditing;

/// <summary>
/// Origem das alterações do escopo atual quando não há usuário logado (MIG-01a CA 21): a importação define
/// "Importação Dolibarr · operador · execução" e toda entrada de auditoria do escopo leva esse texto.
/// </summary>
public sealed class AuditOrigin
{
    public string? Label { get; set; }

    /// <summary>
    /// Escopo de anonimização do titular (LGPD-01b, ADR 0019 §2): com o número do pedido, toda alteração do escopo vai para a auditoria com a
    /// ação <c>Anonymized</c> e só os nomes dos campos (<c>{ "campo": { "anonymized": true } }</c>), nunca o valor anterior nem o novo.
    /// </summary>
    public string? AnonymizationRequest { get; set; }
}
