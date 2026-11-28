using BemAqui.Commercial.Domain;
using BemAqui.Inventory.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Application;

// DTOs sem custo (COM-01a CA 2, D7): nenhuma propriedade de custo, margem ou markup; o teste de arquitetura confere.

public sealed record QuoteSummary(Guid Id, string Number, int Revision, QuoteStatus Status, Guid PartyId, string PartyName, string? MainItem, decimal TotalPrice, DateTimeOffset? SentAt, DateTimeOffset CreatedAt, DateOnly? ValidUntil, QuoteLostReason? LostReason, int Revisions = 1);

public sealed record QuotePage(IReadOnlyList<QuoteSummary> Items, int Page, int PageSize, int Total);

public sealed record QuoteItemView(Guid Id, int Sequence, QuoteItemKind Kind, Guid? ItemId, string? ItemName, string Description, decimal Quantity, decimal UnitPrice, bool PriceOverridden, decimal Total, string? UnitCode = null);

public sealed record QuoteDetail(
    Guid Id, string Number, int Revision, QuoteStatus Status, Guid PartyId, string PartyName, Guid? ContactId, Guid? SiteAddressId, int? DeliveryDays, string? Notes,
    QuotePricingMode PricingMode, IReadOnlyList<QuoteItemView> Items, decimal TotalPrice, DateTimeOffset? SentAt, DateTimeOffset CreatedAt, DateOnly? ValidUntil, QuoteLostReason? LostReason, string? LostDetail,
    bool IsCurrent, Guid? ParentQuoteId, string? CopiedFromNumber, uint Version, ExecutionKind ExecutionKind = ExecutionKind.Fabrication);

// DTOs com custo: só com quotes.view_cost, em rotas próprias.

public sealed record QuoteCostSummary(Guid Id, decimal TotalCost, decimal? MarginPct);

public sealed record QuoteCostLineView(
    Guid Id, CostType CostType, string? Description, Guid? ItemId, string? ItemName, string? UnitCode, Guid? WorkCenterId, Guid? PositionId, string? PositionName, Guid? PartyId,
    decimal Quantity, decimal UnitCost, decimal Total);

public sealed record QuoteItemCostView(Guid ItemId, decimal UnitCost, decimal? Pct, decimal EffectivePct, decimal? SuggestedPrice, IReadOnlyList<QuoteCostLineView> Lines);

public sealed record QuoteCostDetail(Guid QuoteId, QuotePricingMode PricingMode, decimal? DefaultPct, decimal TotalCost, decimal TotalPrice, decimal? MarginPct, IReadOnlyList<QuoteItemCostView> Items, bool StaleLaborCosts = false);

// Entradas.

public sealed record QuoteCreateInput(Guid PartyId, Guid? ContactId, Guid? SiteAddressId);

public sealed record QuoteHeaderInput(Guid? ContactId, Guid? SiteAddressId, int? DeliveryDays, string? Notes, uint Version);

/// <summary>Item (CA 7 e 14): <c>UnitPrice</c> preenchido vale como digitado à mão; <c>ResetPrice</c> volta ao sugerido.</summary>
public sealed record QuoteItemInput(QuoteItemKind Kind, Guid? ItemId, string Description, decimal Quantity, decimal? UnitPrice, bool ResetPrice, uint Version);

public sealed record QuoteCostLineInput(CostType CostType, string? Description, Guid? ItemId, Guid? WorkCenterId, Guid? PositionId, Guid? PartyId, decimal Quantity, decimal? UnitCost);

/// <summary>Composição inteira do item (CA 8): substitui as linhas; <c>Pct</c> nulo usa o % padrão do tenant.</summary>
public sealed record QuoteCostsInput(IReadOnlyList<QuoteCostLineInput> Lines, decimal? Pct, uint Version);

public sealed record VersionInput(uint Version);

/// <summary>Linha de mão de obra com custo-hora diferente do atual do cargo (CA 13).</summary>
public sealed record CostRefreshLine(Guid ItemId, string ItemDescription, Guid LineId, string? PositionName, decimal Hours, decimal OldUnitCost, decimal NewUnitCost, decimal OldTotal, decimal NewTotal);

public sealed record CostRefreshPreview(IReadOnlyList<CostRefreshLine> Lines, decimal OldTotalCost, decimal NewTotalCost);

public sealed record QuotePartyOption(Guid Id, string Name, string? Document);

/// <summary>
/// Item do seletor do orçamento (PRC-00): tipo e unidade para a linha nascer certa; <c>AverageCost</c> (custo médio, <c>numeric(18,6)</c>) só
/// para quem tem <c>quotes.view_cost</c>, nulo para os demais (filtrado no servidor, D7).
/// </summary>
public sealed record QuoteItemOption(Guid Id, string Code, string Name, string UnitCode, ItemType Type, decimal? AverageCost);

public sealed record LaborPositionOption(Guid Id, string Name, Guid? DefaultWorkCenterId, decimal HourlyCost);

public sealed record LaborWorkCenterOption(Guid Id, string Name);

/// <summary>EXE-01a CA 14: <c>AcceptanceSegregation</c> = quem registrou a entrega não registra o aceite (desligada por padrão).</summary>
/// <summary>EXE-03b CA 5: <c>StageSelfApprovalMode</c> = quem concluiu a etapa pode aprová-la (padrão Só o Proprietário).</summary>
public sealed record CommercialParametersView(
    decimal? DefaultPct, string PricingMode, int ValidityDays = 15, bool AcceptanceSegregation = false,
    BemAqui.Finance.Contracts.SelfApprovalMode StageSelfApprovalMode = BemAqui.Finance.Contracts.SelfApprovalMode.OwnerOnly);

/// <summary>
/// EXE-01a CA 14: <c>AcceptanceSegregation</c> nulo mantém o valor atual. EXE-03b CA 5: <c>StageSelfApprovalMode</c> nulo mantém; trocar exige o
/// Proprietário, o modo visto (<c>StageSelfApprovalModeSeen</c>) e o código do autenticador (<c>Code</c>), como na FIN-10a.
/// </summary>
public sealed record CommercialParametersInput(
    decimal? DefaultPct, int ValidityDays = 15, bool? AcceptanceSegregation = null, BemAqui.Finance.Contracts.SelfApprovalMode? StageSelfApprovalMode = null,
    BemAqui.Finance.Contracts.SelfApprovalMode? StageSelfApprovalModeSeen = null, string? Code = null);

public sealed record QuoteTermOption(Guid Id, string Name);

public sealed record MaterialCost(decimal? UnitCost);
