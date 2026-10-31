using BemAqui.Production.Contracts;

namespace BemAqui.Production.Application;

/// <summary>Linha pedida no romaneio (CA 1 e 2): com série, uma série por unidade; "Estoque geral" para o produto acabado sem projeto.</summary>
public sealed record ShipmentLineInput(Guid ItemId, Guid LocationId, Guid? LotId, decimal Quantity, IReadOnlyList<Guid>? SerialIds, bool FromGeneralStock = false);

/// <summary>"Salvar romaneio" (CA 1): projeto, entrega, transporte e linhas. <c>Version</c> só na alteração do rascunho.</summary>
public sealed record ShipmentInput(
    Guid ProjectId, string? DeliveryAddress, Guid? CarrierPartyId, string? VehiclePlate, string? DriverName, int? Volumes, decimal? GrossWeight, DateOnly? PlannedDate,
    IReadOnlyList<ShipmentLineInput> Lines, uint? Version = null);

/// <summary>Confirmar a expedição ou o estoque geral (CA 4 e 6): só a versão (quem confirma decide sobre o que foi montado, sem editar).</summary>
public sealed record ShipmentDecisionInput(uint Version);

/// <summary>Cancelar o romaneio ou estornar a expedição (CA 10 e 11): motivo obrigatório.</summary>
public sealed record ShipmentReasonInput(string? Reason, uint Version);

/// <summary>Série na tela: o número, a OS que a produziu e o romaneio não cancelado em que já está (para a montagem).</summary>
public sealed record ShipmentSerialView(Guid Id, string SerialNumber, Guid? ProductionOrderId, string? OpenShipmentNumber);

/// <summary>Linha do romaneio na tela (sem valor nem custo).</summary>
public sealed record ShipmentLineView(
    Guid Id, int Sequence, Guid ItemId, string ItemCode, string ItemName, string UnitCode, decimal Quantity, Guid LocationId, string LocationName, Guid? LotId, string? LotNumber,
    bool FromGeneralStock, IReadOnlyList<ShipmentSerialView> Serials, Guid? MovementId);

/// <summary>
/// Próximos passos do romaneio (CA 8): a OS de instalação do projeto (COM-06) ou o registro de entrega (COM-04b) e o motivo que impede
/// confirmar ou confirmar o estoque geral (botão desabilitado com o motivo). FIS-03a CA 1 e 15: a NF-e ativa do romaneio (com link) ou, sem nota,
/// o "Emitir NF-e" no romaneio expedido.
/// </summary>
public sealed record ShipmentNext(
    bool CanConfirm, string? ConfirmBlocker, bool CanConfirmStock, string? ConfirmStockBlocker, Guid? InstallationOrderId, string? InstallationOrderNumber, bool ProjectDelivered,
    string ProjectStatus, Guid? InvoiceId = null, string? InvoiceLabel = null, string? InvoiceStatus = null);

/// <summary>Romaneio (CA 1 a 17): cabeçalho, entrega, transporte (placa e motorista só aqui, nunca na lista), linhas, quem montou e quem confirmou.</summary>
public sealed record ShipmentDetail(
    Guid Id, string Number, ShipmentState Status, Guid ProjectId, string ProjectNumber, string ProjectName, Guid PartyId, string PartyName, string DeliveryAddress,
    Guid? CarrierPartyId, string? CarrierName, string? VehiclePlate, string? DriverName, int? Volumes, decimal? GrossWeight, DateOnly? PlannedDate, bool HasGeneralStock,
    DateTimeOffset CreatedAt, Guid? CreatedBy, string? CreatedByName, DateTimeOffset? StockApprovalRequestedAt, string? StockApprovedByName, bool SelfApproved,
    DateTimeOffset? ShippedAt, string? ShippedByName, DateTimeOffset? CancelledAt, string? CancelledByName, string? CancelReason, DateTimeOffset? ReversedAt,
    string? ReversedByName, string? ReversalReason, Guid? PdfAttachmentId, IReadOnlyList<ShipmentLineView> Lines, ShipmentNext Next, uint Version);

/// <summary>Romaneio na lista (CA 13): número, projeto, cliente, itens, data e situação; sem placa nem motorista.</summary>
public sealed record ShipmentRow(
    Guid Id, string Number, ShipmentState Status, Guid ProjectId, string ProjectNumber, string PartyName, int Lines, decimal Units, DateOnly? PlannedDate, DateTimeOffset? ShippedAt,
    DateTimeOffset CreatedAt, bool HasGeneralStock);

/// <summary>Página da lista; <c>AwaitingMine</c> é o total do filtro "Aguardando você" (estoque geral a confirmar por outra pessoa).</summary>
public sealed record ShipmentPage(IReadOnlyList<ShipmentRow> Items, int Total, int Page, int PageSize, int AwaitingMine);

/// <summary>Produto acabado pronto para expedir numa posição (CA 1 e 13): físico, reservado e disponível; sem custo.</summary>
public sealed record ReadyToShipLine(
    Guid ItemId, string ItemCode, string ItemName, string UnitCode, Guid LocationId, string LocationName, Guid? LotId, string? LotNumber, bool TracksSerial, decimal Quantity,
    decimal OnHand, decimal Reserved, decimal Available, decimal Shippable, decimal InOpenShipments, bool FromGeneralStock, IReadOnlyList<ShipmentSerialView> Serials);

/// <summary>"Prontos para expedir" (CA 13): projeto, cliente e o que dá para expedir agora.</summary>
public sealed record ReadyToShipProject(Guid ProjectId, string ProjectNumber, string ProjectName, string PartyName, decimal Shippable, IReadOnlyList<ReadyToShipLine> Lines);

/// <summary>Transportadora no seletor (CA 1): a última usada com o cliente leva a tag "Sugerido".</summary>
public sealed record CarrierOption(Guid Id, string Name, bool Suggested);

/// <summary>
/// Formulário do "Novo romaneio" já preenchido (CA 1, padrão 6): o projeto, o cliente, o endereço da obra, a transportadora sugerida e o
/// produto acabado do projeto (padrão: todas as séries e quantidades; vindo da OS, as séries da OS).
/// </summary>
public sealed record ShipmentForm(
    Guid ProjectId, string ProjectNumber, string ProjectName, Guid PartyId, string PartyName, string? DeliveryAddress, Guid? SuggestedCarrierId, IReadOnlyList<CarrierOption> Carriers,
    IReadOnlyList<ReadyToShipLine> Available, Guid? ProductionOrderId);

/// <summary>Impacto da confirmação por linha (CA 4): saída, local, séries que passam a "Expedido" e o físico antes → depois.</summary>
public sealed record ShipmentImpactLine(
    Guid LineId, string ItemCode, string ItemName, string UnitCode, string LocationName, decimal Quantity, decimal OnHandBefore, decimal OnHandAfter, IReadOnlyList<string> SerialNumbers,
    bool FromGeneralStock, bool Insufficient);

public sealed record ShipmentImpact(Guid Id, string Number, ShipmentState Status, bool RequiresStockApproval, IReadOnlyList<ShipmentImpactLine> Lines, uint Version);
