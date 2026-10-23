using System.Text.Json;
using System.Text.Json.Serialization;
using BemAqui.Inventory.Domain;

namespace BemAqui.Inventory.Application;

/// <summary>Lote do item no local para o diálogo (CA 1): saldo físico do lote e se é o sugerido (validade mais próxima, depois o mais antigo).</summary>
public sealed record RequisitionLotOption(Guid LotId, string LotNumber, DateOnly? Expiry, decimal OnHand, bool Suggested);

/// <summary>Parte sugerida da linha num lote (CA 1): o diálogo já vem dividido quando o lote sugerido não basta.</summary>
public sealed record RequisitionLotPick(Guid LotId, string LotNumber, decimal Quantity, bool Suggested);

/// <summary>
/// Linha do diálogo "Requisitar material" (CA 1): a reserva ativa da OS no local, com o reservado a baixar (padrão: todo), o disponível
/// livre no local (físico − reservado), o custo médio do momento (para o total do impacto), os lotes do local e a divisão sugerida.
/// </summary>
public sealed record RequisitionDraftLine(
    Guid? ReservationId, Guid ItemId, string ItemCode, string ItemName, string UnitCode, bool TracksLot, Guid LocationId, string LocationCode, string LocationName,
    decimal Reserved, decimal OnHand, decimal Available, decimal AverageCost, IReadOnlyList<RequisitionLotOption> Lots, IReadOnlyList<RequisitionLotPick> Picks);

/// <summary>
/// Diálogo "Requisitar material" (CA 1 a 4): OS, projeto, o limite do consumo acima da BOM (<c>inventory.manual_approval_amount</c>) e as
/// linhas das reservas ativas. <c>OrderClosed</c>: OS concluída ou cancelada não recebe material.
/// </summary>
public sealed record RequisitionDraft(
    Guid ProductionOrderId, string? OrderNumber, Guid ProjectId, string? ProjectNumber, bool OrderClosed, bool BomReleased, decimal ApprovalLimit, IReadOnlyList<RequisitionDraftLine> Lines);

/// <summary>Saldo de um item por local para o "Material extra" (CA 3): disponível livre, lotes e custo médio.</summary>
public sealed record RequisitionItemLocation(Guid LocationId, string LocationCode, string LocationName, bool IsDefault, decimal OnHand, decimal Available, IReadOnlyList<RequisitionLotOption> Lots);

public sealed record RequisitionItemStock(Guid ItemId, string ItemCode, string ItemName, string UnitCode, bool TracksLot, decimal AverageCost, IReadOnlyList<RequisitionItemLocation> Locations);

/// <summary>
/// Linha enviada (CA 2 e 3): a reserva (nula no material extra), item, local, lote (obrigatório no item com lote), quantidade na unidade
/// de estoque e o motivo do consumo acima da BOM (obrigatório quando passa do reservado).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequisitionLineInput(Guid? ReservationId, Guid ItemId, Guid LocationId, Guid? LotId, decimal Quantity, string? OverReason);

/// <summary>"Baixar material" ou "Enviar para aprovação" (CA 2 a 4): o servidor decide pela regra do valor acima da BOM.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequisitionInput(IReadOnlyList<RequisitionLineInput>? Lines);

/// <summary>Confirmar o pedido do operador (CA 7): o almoxarife ajusta lote e quantidade; a versão que a tela mostrou.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequisitionConfirmInput(IReadOnlyList<RequisitionLineInput>? Lines, uint Version);

/// <summary>
/// Decisão do aprovador (CA 4): só o comentário e a versão; qualquer outro campo é recusado com 422 (decide sobre o que foi enviado).
/// </summary>
public sealed record RequisitionDecisionInput(string? Comment, uint Version)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>"Recusar pedido" (CA 7), com o motivo que o operador vê.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequisitionDeclineInput(string? Reason, uint Version);

/// <summary>"Estornar requisição" (CA 14), com o motivo.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequisitionReverseInput(string? Reason, uint Version);

/// <summary>Linha da devolução ou da perda (CA 12 e 13): a linha da baixa original e a quantidade (até o baixado e não devolvido).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReturnLineInput(Guid IssueLineId, decimal Quantity);

/// <summary>"Devolver ao estoque" (CA 12): as linhas e o local que recebe.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReturnInput(Guid LocationId, IReadOnlyList<ReturnLineInput>? Lines);

/// <summary>"Manter no projeto como perda" (CA 13): só na OS cancelada, com o motivo.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LossInput(string? Reason, IReadOnlyList<ReturnLineInput>? Lines);

public sealed record RequisitionListQuery(MaterialRequisitionStatus? Status, bool AwaitingMe, Guid? ProductionOrderId, MaterialRequisitionKind? Kind);

/// <summary>
/// Requisição na lista (padrão 5, "Aguardando você"): número, tipo, situação, OS, projeto, etapa do pedido do operador, quantas linhas,
/// o primeiro item, o valor baixado e o acima da BOM, quem pediu e quando.
/// </summary>
public sealed record RequisitionSummary(
    Guid Id, string Number, MaterialRequisitionKind Kind, MaterialRequisitionStatus Status, Guid ProductionOrderId, string? OrderNumber, Guid ProjectId, string? ProjectNumber,
    string? StepName, int LineCount, string? FirstItem, decimal Value, decimal OverBomValue, string? RequestedByName, DateTimeOffset RequestedAt, bool FromOperator, bool AwaitingMe);

/// <summary>
/// Linha da requisição (CA 17): item, local, lote, quantidade, excedente com o motivo, o movimento e o valor (custo da baixa, ou o da
/// baixa original na devolução) e o que ainda pode voltar ao estoque.
/// </summary>
public sealed record RequisitionLineView(
    Guid Id, int Sequence, Guid? ReservationId, Guid ItemId, string ItemCode, string ItemName, string UnitCode, Guid LocationId, string LocationCode, string LocationName,
    Guid? LotId, string? LotNumber, decimal Quantity, decimal OverQuantity, string? OverReason, bool IsExtra, Guid? ReturnsLineId, Guid? MovementId, string? MovementNumber,
    decimal? UnitCost, decimal? Amount, decimal Returnable);

/// <summary>
/// Detalhe da requisição (CA 4, 7, 14 e 17): quem pediu, quem confirmou ou baixou, quem decidiu, o estorno, as linhas e o que o usuário
/// pode fazer agora (com o motivo quando não pode).
/// </summary>
public sealed record RequisitionDetail(
    Guid Id, string Number, MaterialRequisitionKind Kind, MaterialRequisitionStatus Status, Guid ProductionOrderId, string? OrderNumber, Guid ProjectId, string? ProjectNumber,
    Guid? StepId, string? StepName, string? RequestedByName, DateTimeOffset RequestedAt, string? CompletedByName, DateTimeOffset? CompletedAt, string? DecidedByName,
    DateTimeOffset? DecidedAt, string? DecisionComment, bool SelfApproved, string? RejectReason, decimal OverBomValue, decimal Value, string? ReversedByName,
    DateTimeOffset? ReversedAt, string? ReversalReason, bool ReversalSelfApproved, IReadOnlyList<RequisitionLineView> Lines, bool CanConfirm, bool CanDecide,
    string? DecideBlockedReason, bool CanReverse, string? ReverseBlockedReason, uint Version);

/// <summary>Linha baixada para a OS que ainda pode voltar ao estoque ou virar perda (CA 12 e 13), pelo custo da baixa original.</summary>
public sealed record ReturnableLine(
    Guid IssueLineId, Guid RequisitionId, string RequisitionNumber, Guid ItemId, string ItemCode, string ItemName, string UnitCode, Guid? LotId, string? LotNumber,
    Guid LocationId, string LocationCode, decimal Issued, decimal Returnable, decimal UnitCost);

/// <summary>Requisições da OS na aba Material (CA 12, 13 e 17): a lista, o que pode voltar e se a OS foi cancelada (perda).</summary>
public sealed record OrderRequisitions(Guid ProductionOrderId, bool OrderClosed, bool OrderCancelled, IReadOnlyList<RequisitionSummary> Requisitions, IReadOnlyList<ReturnableLine> Returnable);

/// <summary>"Para separar hoje" (propositivo): OS em aberto com material reservado a baixar, a etapa atual e o pedido do operador em aberto.</summary>
public sealed record PickingRow(
    Guid ProductionOrderId, string? OrderNumber, Guid ProjectId, string? ProjectNumber, string? CurrentStepName, int Items, decimal Reserved, int OpenRequests);

/// <summary>Linha Material da aba Custos da OS (CA 10): custo baixado por item, lido do <c>IStockMovements.CostByOrderAsync</c>.</summary>
public sealed record OrderMaterialCostItem(Guid ItemId, string ItemCode, string ItemName, string UnitCode, decimal Quantity, decimal Amount);

public sealed record OrderMaterialCostView(Guid ProductionOrderId, IReadOnlyList<OrderMaterialCostItem> Items, decimal Total);
