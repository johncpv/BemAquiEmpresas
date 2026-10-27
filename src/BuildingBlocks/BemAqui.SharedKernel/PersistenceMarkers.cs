namespace BemAqui.SharedKernel;

/// <summary>Toda alteração da entidade é gravada em <c>audit.audit_log</c> na mesma transação (diff antes/depois).</summary>
public interface IAuditable;

/// <summary>
/// Auditável só nas mudanças (BNK-03a): a criação em massa (ex.: as linhas do extrato do banco) já fica registrada no documento que
/// a trouxe (o lote), então o <c>Created</c> não gera linha na auditoria; as alterações seguintes, sim.
/// </summary>
public interface IAuditUpdatesOnly : IAuditable;

/// <summary>Cadastro com exclusão lógica: excluir marca <see cref="IsDeleted"/> e as consultas padrão o ignoram.</summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; }

    DateTimeOffset? DeletedAt { get; }

    /// <summary>Chamado pela infraestrutura ao converter um Delete em exclusão lógica.</summary>
    void MarkDeleted(DateTimeOffset at);
}

/// <summary>Dado pessoal (LGPD): no diff da auditoria o valor aparece mascarado.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PersonalDataAttribute : Attribute;

/// <summary>Coluna com segredo cifrado (FND-11): a auditoria registra só que mudou, nunca o valor.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ProtectedSecretAttribute : Attribute;

/// <summary>Coluna grande gravada uma vez (ex.: snapshot do contrato, COM-04a CA 13): a auditoria registra só que mudou, sem o conteúdo.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AuditOnCreateOnlyAttribute : Attribute;

/// <summary>
/// Coluna mantida por um motor, fora do diff da auditoria (ADR 0023 §4): o custo médio do item muda a cada movimento e o
/// movimento de estoque é a trilha. Mudança só nessas colunas não gera linha na auditoria.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotAuditedAttribute : Attribute;
