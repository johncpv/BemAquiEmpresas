namespace BemAqui.SharedKernel;

/// <summary>Toda alteração da entidade é gravada em <c>audit.audit_log</c> na mesma transação (diff antes/depois).</summary>
public interface IAuditable;

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
