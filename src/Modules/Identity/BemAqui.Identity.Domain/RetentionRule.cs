using BemAqui.SharedKernel;

namespace BemAqui.Identity.Domain;

/// <summary>
/// Linha da tabela de retenção (<c>settings.retention_rules</c>, LGPD-01b CA 1, ADR 0018): por tipo de titular e grupo de campos, a regra
/// (a qualquer momento ou depois de N anos de uma data base). Pré-configurada pela ADR 0018 (<c>seed_key</c>) e editável pelo Proprietário;
/// cada linha tem o "A confirmar com a contadora e com o jurídico" até alguém marcar como conferida (quem e quando). Nunca é apagada.
/// </summary>
[DocumentName("A regra de retenção")]
public sealed class RetentionRule : Entity, IAuditable, IConcurrencyVersioned
{
    public const int MaxYears = 30;

    public PersonalDataSubjectKind SubjectType { get; init; }

    public string FieldGroup { get; init; } = string.Empty;

    public RetentionRuleKind Rule { get; set; }

    public int Years { get; set; }

    /// <summary>Data base da regra (fixa pelo grupo: o último pagamento, o desligamento ou o fim do exercício).</summary>
    public RetentionBase BaseDate { get; init; }

    public DateTimeOffset? ConfirmedAt { get; set; }

    public Guid? ConfirmedBy { get; set; }

    /// <summary><c>{tipo}.{grupo}</c>: a linha do seed, para o semeador só criar o que falta.</summary>
    public string SeedKey { get; init; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }

    public uint Version { get; set; }
}
