using System.Globalization;
using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Inventory.Contracts;

/// <summary>Situação da série (<c>inventory.serials.status</c>, PRD-06a2): no estoque, expedida (PRD-06b) ou cancelada (estorno da conclusão).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SerialState>))]
public enum SerialState
{
    InStock,
    Shipped,
    Cancelled,
}

/// <summary>Série vista pelos outros módulos, sem custo. <c>ShipmentId</c> é o romaneio que a expediu (PRD-06b).</summary>
public sealed record SerialInfo(
    Guid Id, Guid ItemId, string SerialNumber, Guid? ProductionOrderId, Guid? ProjectId, Guid? LocationId, SerialState Status, Guid? EntryMovementId, DateTimeOffset CreatedAt,
    Guid? ShipmentId = null, DateTimeOffset? ShippedAt = null);

/// <summary>
/// Séries de uma OS concluída (PRD-06a2 CA 4 e 5): o item, a OS, o projeto, o local e o movimento de entrada. <c>SerialNumbers</c> nulo
/// gera os números pela sequência do item e do ano (D15); preenchido, usa os números editados (um por unidade, únicos no item).
/// </summary>
public sealed record SerialCreate(Guid ItemId, int Units, IReadOnlyList<string>? SerialNumbers, Guid ProductionOrderId, Guid ProjectId, Guid LocationId, Guid EntryMovementId);

/// <summary>
/// Entrada do produto acabado de uma OS (o movimento <c>Production</c>), para a aba Séries, a linha do tempo e o estorno. <c>Leg</c> é a
/// perna da chave de idempotência: 1 na primeira conclusão e + 1 a cada reconclusão depois de um estorno.
/// </summary>
public sealed record FinishedGoodsEntry(
    Guid MovementId, string Number, Guid ItemId, Guid LocationId, decimal Quantity, decimal UnitCost, decimal TotalCost, DateTimeOffset PostedAt, bool Reversed, int Leg);

/// <summary>
/// Séries do produto acabado (PRD-06a2, ADR 0023 §1): criar, cancelar e consultar, na transação do chamador (a Produção é dona da
/// transação da conclusão e do estorno). Exige transação aberta para gravar (<see cref="InvalidOperationException"/> sem ela).
/// </summary>
public interface IStockSerials
{
    /// <summary>Próximos números sugeridos para o item (sem consumir a sequência), pulando os que já existem: o diálogo mostra e deixa editar.</summary>
    Task<IReadOnlyList<string>> SuggestAsync(Guid itemId, int count, CancellationToken cancellationToken = default);

    /// <summary>Grava as séries <c>InStock</c>; 422 se um número repete no item ou na lista.</summary>
    Task<Result<IReadOnlyList<SerialInfo>>> CreateAsync(SerialCreate create, CancellationToken cancellationToken = default);

    /// <summary>
    /// Estorno da conclusão (CA 10): as séries da OS no estoque vão a <c>Cancelled</c>; com alguma <c>Shipped</c>, 409
    /// <c>serial_shipped</c> "O produto já foi expedido." e nada muda. Devolve quantas foram canceladas.
    /// </summary>
    Task<Result<int>> CancelForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default);

    /// <summary>Séries da OS (todas as situações), na ordem do número.</summary>
    Task<IReadOnlyList<SerialInfo>> ForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default);

    /// <summary>A entrada <c>Production</c> mais recente da OS (com a marca de estornada) ou null.</summary>
    Task<FinishedGoodsEntry?> EntryAsync(Guid productionOrderId, CancellationToken cancellationToken = default);

    /// <summary>Séries por id (todas as situações); as que não existem ficam fora.</summary>
    Task<IReadOnlyList<SerialInfo>> FindAsync(IReadOnlyCollection<Guid> serialIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirmação do romaneio (PRD-06b CA 4, D4): as séries vão a <c>Shipped</c> com o romaneio, na transação do chamador (depois da
    /// saída <c>Shipment</c> pelo <see cref="IStockMovements"/>, que trava o item). Alguma fora do estoque: 409 <c>serial_not_in_stock</c>
    /// e nada muda. Devolve quantas foram expedidas.
    /// </summary>
    Task<Result<int>> ShipAsync(Guid shipmentId, IReadOnlyCollection<Guid> serialIds, CancellationToken cancellationToken = default);

    /// <summary>Estorno da expedição (PRD-06b CA 11): as séries expedidas pelo romaneio voltam a <c>InStock</c>, no mesmo local.</summary>
    Task<Result<int>> UnshipAsync(Guid shipmentId, IReadOnlyCollection<Guid> serialIds, CancellationToken cancellationToken = default);
}

/// <summary>Lote consumido pela OS (CA 8): item, lote (nulo sem controle de lote) e quantidade líquida, na unidade de estoque. Sem custo.</summary>
public sealed record ConsumedLot(Guid ItemId, Guid? LotId, string? LotNumber, decimal Quantity);

/// <summary>
/// Rastreabilidade série → lotes de matéria-prima (PRD-06a2 CA 8, ponto 87, consultada pela EST-04): montada na conclusão a partir dos
/// movimentos <c>Issue</c> − <c>Return</c> da OS; cada série herda os lotes da OS inteira.
/// </summary>
public interface IStockTraceability
{
    /// <summary>Grava o vínculo da OS na transação do chamador (idempotente: com vínculo vigente, devolve o que existe).</summary>
    Task<IReadOnlyList<ConsumedLot>> RecordOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default);

    /// <summary>Estorno da conclusão (CA 10): o vínculo vigente da OS fica marcado como estornado.</summary>
    Task ReverseOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default);

    /// <summary>Lotes consumidos pela OS (vínculo vigente).</summary>
    Task<IReadOnlyList<ConsumedLot>> ForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default);

    /// <summary>Lotes de uma série (os da OS que a produziu); vazio se a série não existe ou foi cancelada.</summary>
    Task<IReadOnlyList<ConsumedLot>> ForSerialAsync(Guid serialId, CancellationToken cancellationToken = default);
}

/// <summary>Regras da série sem banco (PRD-06a2 CA 4, D15 ponto 80), usadas pelo Inventory e pela conclusão da OS.</summary>
public static class SerialRules
{
    public const int Digits = 5;
    public const int MaxLength = 60;

    /// <summary><c>{código do item}-{AAAA}-{seq}</c>, com a sequência em 5 dígitos: <c>ELV-0001-2026-00001</c>.</summary>
    public static string Format(string itemCode, int year, long sequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemCode);
        return $"{itemCode.Trim()}-{year.ToString(CultureInfo.InvariantCulture)}-{sequence.ToString(new string('0', Digits), CultureInfo.InvariantCulture)}";
    }

    /// <summary>Produto com série entra em unidades inteiras: a quantidade vira o número de séries; null se não é inteira e positiva.</summary>
    public static int? Units(decimal quantity) => quantity > 0 && quantity == decimal.Truncate(quantity) && quantity <= 10_000 ? (int)quantity : null;

    /// <summary>
    /// Confere as séries editadas antes de confirmar (CA 4): uma por unidade, sem vazio, até 60 caracteres e sem repetir na lista. Devolve
    /// a mensagem do 422 ou null; a unicidade no item é conferida no banco.
    /// </summary>
    public static string? Invalid(IReadOnlyList<string> serials, int units)
    {
        ArgumentNullException.ThrowIfNull(serials);
        if (serials.Count != units)
        {
            return $"Informe {units} {(units == 1 ? "número de série" : "números de série")}, um por unidade.";
        }

        if (serials.Any(string.IsNullOrWhiteSpace))
        {
            return "Número de série vazio.";
        }

        if (serials.Any(s => s.Trim().Length > MaxLength))
        {
            return $"Número de série com até {MaxLength} caracteres.";
        }

        var repeated = serials.Select(s => s.Trim()).GroupBy(s => s, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        return repeated is null ? null : $"O número de série {repeated.Key} se repete.";
    }
}
