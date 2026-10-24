using BemAqui.Inventory.Contracts;
using BemAqui.Purchasing.Contracts;

namespace BemAqui.Purchasing.Application;

/// <summary>Linha da SC manual (CA 3): item ativo, quantidade na unidade de estoque ou noutra com conversão, e a necessidade (≥ hoje).</summary>
public sealed record PurchaseRequestLineInput(Guid ItemId, decimal Quantity, Guid? UnitId, DateOnly NeededBy);

/// <summary>
/// "Enviar solicitação" (CA 3): destino = projeto em andamento ou estoque geral (<c>ProjectId</c> null); a OS é opcional (quando a SC
/// nasce da falta de uma OS) e precisa ser do mesmo projeto.
/// </summary>
public sealed record PurchaseRequestInput(Guid? ProjectId, Guid? ProductionOrderId, string? Notes, IReadOnlyList<PurchaseRequestLineInput>? Lines);

public sealed record PurchaseRequestLineView(
    Guid Id, int Sequence, Guid ItemId, string ItemCode, string ItemName, string UnitCode, decimal Quantity, decimal? EnteredQuantity, string? EnteredUnitCode,
    DateOnly NeededBy, PurchaseRequestStatus Status, Guid? ProductionOrderId, string? OrderNumber, Guid? BomLineId, bool ManuallyEdited, bool CalculatedOnOnboarding,
    string? CancelReason, uint Version);

/// <summary>Detalhe da SC (CA 12 a 14, 17): o que o comprador vê e o que pode fazer (editar e cancelar com <c>purchasing.edit</c>).</summary>
public sealed record PurchaseRequestDetail(
    Guid Id, string Number, PurchaseRequestOrigin Origin, PurchaseRequestStatus Status, Guid? ProjectId, string? ProjectNumber, string? ProjectName, Guid? ProductionOrderId,
    string? OrderNumber, string? Notes, Guid? RequestedBy, string? RequestedByName, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? CancelReason,
    DateTimeOffset? CancelledAt, string? CancelledByName, DateOnly? NearestNeededBy, bool CanEdit, bool CanCancel, IReadOnlyList<PurchaseRequestLineView> Lines, uint Version);

/// <summary>Linha da lista "Solicitações" (CA 12).</summary>
public sealed record PurchaseRequestSummary(
    Guid Id, string Number, PurchaseRequestOrigin Origin, PurchaseRequestStatus Status, Guid? ProjectId, string? ProjectNumber, string? ProjectName, Guid? ProductionOrderId,
    string? OrderNumber, string ItemSummary, int LineCount, int OpenLineCount, DateOnly? NearestNeededBy, DateTimeOffset CreatedAt);

/// <summary>
/// Filtros da lista (na URL): <c>Show</c> = <c>aguardando-voce</c> (SC aberta, sem cotação nem pedido; padrão de quem tem
/// <c>purchasing.edit</c>), <c>abertas</c> (aberta ou em cotação) ou <c>todas</c>.
/// </summary>
public sealed record PurchaseRequestListQuery(
    string? Show, PurchaseRequestOrigin? Origin, PurchaseRequestStatus? Status, Guid? ProjectId, Guid? ItemId, Guid? ProductionOrderId, string? Term);

/// <summary>Uma SC dentro da visão "Por item".</summary>
public sealed record PurchaseItemRequest(Guid RequestId, string Number, PurchaseRequestOrigin Origin, decimal Quantity, DateOnly NeededBy, string? ProjectNumber, string? OrderNumber);

/// <summary>Visão "Por item" (CA 12): as linhas abertas de todas as SC somadas por item (o comprador compra por item).</summary>
public sealed record PurchaseItemRow(
    Guid ItemId, string ItemCode, string ItemName, string UnitCode, decimal OpenQuantity, DateOnly NearestNeededBy, int RequestCount, IReadOnlyList<PurchaseItemRequest> Requests);

/// <summary>Editar a linha aberta (CA 13), com a versão da linha (409 se mudou).</summary>
public sealed record PurchaseRequestLineUpdate(decimal Quantity, DateOnly NeededBy, uint Version);

/// <summary>Resultado da edição: a SC e o aviso "A OS {número} continua com falta de {x}." quando a linha da falta ficou menor.</summary>
public sealed record PurchaseRequestLineUpdated(PurchaseRequestDetail Request, string? Warning);

/// <summary>"Cancelar solicitação" ou a linha (CA 14): motivo obrigatório e a versão do documento.</summary>
public sealed record PurchaseRequestCancelInput(string? Reason, uint Version);

/// <summary>Item para a linha da SC (CA 3): só material ativo, com as unidades em que pode ser informado.</summary>
public sealed record PurchaseItemOption(Guid Id, string Code, string Name, ItemType Type, Guid UnitId, string UnitCode, IReadOnlyList<ItemUnitOption> Units);

public sealed record PurchaseProjectOption(Guid Id, string Number, string Name);

/// <summary>
/// Formulário "Nova solicitação" já preenchido (propositivo): necessidade = hoje + prazo padrão (ou a data prometida − prazo, com a OS),
/// destino e item da falta quando a SC nasce da OS.
/// </summary>
public sealed record PurchaseRequestDefaults(
    DateOnly NeededBy, int LeadDays, Guid? ProjectId, string? ProjectNumber, string? ProjectName, Guid? ProductionOrderId, string? OrderNumber, PurchaseItemOption? Item,
    decimal? Quantity);

/// <summary>Parâmetros de Compras na tela Configurações → Parâmetros (o id leva ao histórico).</summary>
/// <summary>
/// Parâmetros de Compras (CMP-01; CMP-03a): o prazo padrão e a conta e a natureza dos títulos do pedido e do frete (padrão 2.1/Material e
/// 2.4/Freight), com a marca de conferido com a contadora e as contas analíticas para o seletor.
/// </summary>
public sealed record PurchasingParametersView(
    int DefaultLeadDays, Guid? DefaultLeadDaysId, string PayableAccountCode, BemAqui.SharedKernel.CostType PayableCostType, string FreightAccountCode,
    BemAqui.SharedKernel.CostType FreightCostType, bool AccountingConfirmed, IReadOnlyList<PurchasingAccountOption> Accounts);

public sealed record PurchasingAccountOption(string Code, string Name);

/// <summary>Campos nulos ficam como estão (a tela de Configurações manda todos).</summary>
public sealed record PurchasingParametersInput(
    int DefaultLeadDays, string? PayableAccountCode = null, BemAqui.SharedKernel.CostType? PayableCostType = null, string? FreightAccountCode = null,
    BemAqui.SharedKernel.CostType? FreightCostType = null, bool? AccountingConfirmed = null);
