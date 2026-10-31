using System.Text.Json.Serialization;
using BemAqui.Fiscal.Domain;

namespace BemAqui.Fiscal.Application.TaxRules;

/// <summary>Os campos da regra fiscal, como a tela lê e grava (CA 2). O CRT da regra sai de Configurações → Fiscal.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TaxRuleValues(
    TaxOperation Operation,
    string Name,
    string NatureOfOperation,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    string? CfopInternal,
    string? CfopInterstate,
    IcmsParameters? Icms,
    IcmsStParameters? IcmsSt,
    FcpParameters? Fcp,
    IpiParameters? Ipi,
    PisCofinsParameters? Pis,
    PisCofinsParameters? Cofins,
    IssParameters? Iss,
    IbsCbsParameters? IbsCbs,
    DifalParameters? Difal,
    IReadOnlyList<string> CostTaxes,
    string? ComplementaryInfo,
    TaxRuleConditions Conditions);

/// <summary>"Inativar regra" (CA 3): o motivo é obrigatório.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeactivateTaxRuleRequest(string Reason);

/// <summary>Uma linha da lista: a versão que vale (a ativa, ou a última), o rascunho pendente e os selos.</summary>
public sealed record TaxRuleSummary(
    Guid RuleId,
    Guid VersionId,
    int RuleVersion,
    TaxOperation Operation,
    string Name,
    TaxRuleStatus Status,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    string? CfopInternal,
    string? CfopInterstate,
    FiscalReviewState ReviewState,
    bool NeedsReview,
    bool Difal,
    string? ActivatedByName,
    DateTimeOffset? ActivatedAt,
    bool IsSeed,
    Guid? DraftVersionId,
    int? DraftVersion);

/// <summary>Fiscal → Regras fiscais (CA 1, 2 e "Telas e estados"): as regras, os contadores dos filtros e o que o usuário pode fazer.</summary>
public sealed record TaxRuleList(IReadOnlyList<TaxRuleSummary> Rules, int ToConfirm, int ToReview, int Active, bool SeedDraftsOnly, bool CanEdit, bool CanApprove);

/// <summary>Uma versão no histórico da regra.</summary>
public sealed record TaxRuleVersionSummary(Guid VersionId, int RuleVersion, TaxRuleStatus Status, FiscalReviewState ReviewState, DateTimeOffset UpdatedAt, string? ActivatedByName, DateTimeOffset? ActivatedAt);

/// <summary>
/// Uma versão da regra (CA 2 a 4 e 13): valores, situação, quem conferiu, quem ativou, as versões e o que o usuário pode fazer. Quando
/// quem olha não pode ativar esta versão (editou e há outro aprovador), <see cref="ActivateBlockedReason"/> diz por quê.
/// </summary>
public sealed record TaxRuleView(
    Guid RuleId,
    Guid VersionId,
    int RuleVersion,
    TaxRuleStatus Status,
    TaxRuleValues Values,
    int CrtScope,
    FiscalReviewState ReviewState,
    string? ConfirmedByName,
    DateTimeOffset? ConfirmedAt,
    string? ActivatedByName,
    DateTimeOffset? ActivatedAt,
    bool SelfActivated,
    string? DeactivationReason,
    bool NeedsReview,
    bool IsSeed,
    bool SystemAuthored,
    IReadOnlyList<TaxRuleVersionSummary> Versions,
    bool CanEdit,
    bool CanApprove,
    string? ActivateBlockedReason,
    IReadOnlyList<string> IcmsCsts);

/// <summary>
/// "Simular" (CA 10): operação, destinatário e itens; <see cref="RuleVersionId"/> simula uma versão específica (ex.: o rascunho aberto),
/// senão a regra ativa de cada item é resolvida como na emissão. Nada é gravado e o EmissorFiscal não é chamado.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SimulateTaxRequest(
    TaxOperation Operation, string RecipientState, RecipientType RecipientType, bool FinalConsumer, IReadOnlyList<TaxItem> Items, DateOnly? IssueDate = null, Guid? RuleVersionId = null);

/// <summary>A nota simulada (CA 10): a regra de cada item, bases, impostos, totais, custo do projeto, bloqueios e avisos.</summary>
public sealed record TaxSimulationView(TaxCalculation Calculation, string IssuerState, int Crt, IReadOnlyList<string> Warnings);
