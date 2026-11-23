using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Inventory.Domain;

/// <summary>Situação da falta: aberta (o restante não saiu do estoque), aplicada (a baixa foi lançada depois) ou cancelada (o documento de origem foi cancelado).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StockShortfallStatus>))]
public enum StockShortfallStatus
{
    Open,
    Applied,
    Cancelled,
}

/// <summary>
/// Falta de saldo de uma saída que não podia ser recusada (<c>inventory.stock_shortfalls</c>, PDV-05, opção B da PP-02): a venda do caixa já
/// aconteceu, então sai o que há em saldo e o restante fica aqui, aberto, até alguém regularizar o estoque e aplicar a baixa. O motor e o
/// <c>CHECK quantity_on_hand &gt;= 0</c> não mudam (ADR 0023, adendo da PDV-05). Uma linha por documento de origem, item e local; a aplicação
/// lança o movimento pelo motor com a origem <c>StockShortfall</c> e o id da falta; o cancelamento da origem fecha a falta aberta sem movimento
/// e estorna a baixa aplicada. Auditada: a resolução (quem aplicou, quando) fica no histórico.
/// </summary>
[DocumentName("A divergência")]
public sealed class StockShortfall : Entity, IAuditable, IConcurrencyVersioned
{
    public Guid ItemId { get; init; }

    public Guid LocationId { get; init; }

    /// <summary>Quantidade que faltou, na unidade de estoque (<c>decimal(18,4)</c>).</summary>
    public decimal Quantity { get; init; }

    /// <summary>Documento de origem (hoje, <c>Sale</c>: a venda do caixa) e o número legível dele, para o rastreio até a venda.</summary>
    public string SourceType { get; init; } = string.Empty;

    public Guid SourceId { get; init; }

    public string SourceNumber { get; init; } = string.Empty;

    /// <summary>Quando a saída aconteceu (a venda), para o "desde quando" da tela.</summary>
    public DateTimeOffset OccurredAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public StockShortfallStatus Status { get; private set; } = StockShortfallStatus.Open;

    /// <summary>Movimento da baixa aplicada depois (origem <c>StockShortfall</c>, id desta falta).</summary>
    public Guid? MovementId { get; private set; }

    /// <summary>Estorno da baixa aplicada, quando a origem foi cancelada depois de aplicar.</summary>
    public Guid? ReversalMovementId { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public Guid? ResolvedBy { get; private set; }

    public uint Version { get; set; }

    public static StockShortfall Open(Guid itemId, Guid locationId, decimal quantity, string sourceType, Guid sourceId, string sourceNumber, DateTimeOffset occurredAt, DateTimeOffset now) =>
        new()
        {
            ItemId = itemId, LocationId = locationId, Quantity = quantity, SourceType = sourceType, SourceId = sourceId, SourceNumber = sourceNumber, OccurredAt = occurredAt,
            CreatedAt = now,
        };

    public void Apply(Guid movementId, Guid? by, DateTimeOffset at)
    {
        Status = StockShortfallStatus.Applied;
        MovementId = movementId;
        ResolvedBy = by;
        ResolvedAt = at;
    }

    /// <summary>A origem foi cancelada: a falta aberta some sem movimento; a aplicada guarda o estorno.</summary>
    public void Cancel(Guid? reversalMovementId, Guid? by, DateTimeOffset at)
    {
        Status = StockShortfallStatus.Cancelled;
        ReversalMovementId = reversalMovementId;
        ResolvedBy = by;
        ResolvedAt = at;
    }
}
