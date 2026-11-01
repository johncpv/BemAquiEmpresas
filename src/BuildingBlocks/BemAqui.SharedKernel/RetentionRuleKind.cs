using System.Text.Json.Serialization;

namespace BemAqui.SharedKernel;

/// <summary>Regra de retenção de um grupo de campos (LGPD-01b CA 1, ADR 0018): a qualquer momento, ou só depois de N anos de uma data base.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RetentionRuleKind>))]
public enum RetentionRuleKind
{
    Anytime,
    AfterYears,
}

/// <summary>
/// Data base da retenção (CA 1): o último pagamento (dados bancários), o desligamento (dados trabalhistas e previdenciários) ou o fim do
/// exercício do último documento com movimento (nome e documento nas notas e títulos).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RetentionBase>))]
public enum RetentionBase
{
    None,
    LastPayment,
    Termination,
    FiscalYearEnd,
}
