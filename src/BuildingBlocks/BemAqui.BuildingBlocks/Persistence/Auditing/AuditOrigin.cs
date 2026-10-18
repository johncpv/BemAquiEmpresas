namespace BemAqui.BuildingBlocks.Persistence.Auditing;

/// <summary>
/// Origem das alterações do escopo atual quando não há usuário logado (MIG-01a CA 21): a importação define
/// "Importação Dolibarr · operador · execução" e toda entrada de auditoria do escopo leva esse texto.
/// </summary>
public sealed class AuditOrigin
{
    public string? Label { get; set; }
}
