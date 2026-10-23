using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Inventory.Contracts;

/// <summary>Situação do pedido de material vista pelo operador (EST-03 CA 7): aguardando o almoxarifado, separado ou recusado.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MaterialRequestState>))]
public enum MaterialRequestState
{
    Waiting,
    Separated,
    Declined,
}

/// <summary>Item da BOM liberada da OS que o operador pode pedir (CA 5): necessidade, reservado e já baixado, sem custo.</summary>
public sealed record MaterialOptionItem(Guid ItemId, string ItemCode, string ItemName, string UnitCode, decimal Required, decimal Reserved, decimal Issued);

public sealed record MaterialRequestInfoLine(Guid ItemId, string ItemCode, string ItemName, string UnitCode, decimal Quantity);

/// <summary>Pedido de material da OS (CA 5 a 7), sem custo, valor nem lote: número, etapa, situação, motivo da recusa e as linhas.</summary>
public sealed record MaterialRequestInfo(
    Guid Id, string Number, Guid? StepId, MaterialRequestState State, string? DeclineReason, DateTimeOffset RequestedAt, DateTimeOffset? DecidedAt,
    IReadOnlyList<MaterialRequestInfoLine> Lines);

/// <summary>Material da OS para o operador (CA 5): se a BOM foi liberada, os itens dela e os pedidos (da etapa, quando informada).</summary>
public sealed record OrderMaterialOptions(bool BomReleased, IReadOnlyList<MaterialOptionItem> Items, IReadOnlyList<MaterialRequestInfo> Requests);

public sealed record MaterialRequestLine(Guid ItemId, decimal Quantity);

/// <summary>
/// "Pedir material" do operador (CA 5 e 6): a Produção já conferiu a etapa (pronta ou em andamento, da fila dele, OS não cancelada) e
/// manda o nome da etapa e o do operador para o aviso. Quem pede é o usuário logado.
/// </summary>
public sealed record MaterialRequestCommand(Guid ProductionOrderId, Guid StepId, string StepName, string RequesterName, IReadOnlyList<MaterialRequestLine> Lines);

/// <summary>
/// Requisição de material para a Produção (EST-03, ADR 0023 §1): o pedido do operador e a consulta por OS, sem custo. O pedido
/// <b>nunca</b> baixa estoque: nasce <c>Requested</c> e o almoxarife ou o supervisor confirma pelo backoffice (ponto 57). Com o
/// módulo <c>inventory</c> não liberado, 403 <c>module_not_released</c>.
/// </summary>
public interface IMaterialRequisitions
{
    Task<Result<OrderMaterialOptions>> OptionsAsync(Guid productionOrderId, Guid? stepId, CancellationToken cancellationToken = default);

    Task<Result<MaterialRequestInfo>> RequestAsync(MaterialRequestCommand command, CancellationToken cancellationToken = default);
}
