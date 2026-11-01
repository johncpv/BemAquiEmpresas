using System.Text.Json.Serialization;

namespace BemAqui.Assets.Contracts;

/// <summary>Tipo do bem (R-PAT-1): máquina, ferramenta, mobiliário, veículo e TI.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AssetType>))]
public enum AssetType
{
    Machine,
    Tool,
    Furniture,
    Vehicle,
    It,
}

/// <summary>Situação do bem: <c>Active</c> na criação, <c>Maintenance</c> pela OS de manutenção (PAT-03a), <c>Disposed</c> pela baixa (PAT-02).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AssetStatus>))]
public enum AssetStatus
{
    Active,
    Maintenance,
    Disposed,
}

/// <summary>Bem visto pela Produção e pelo Planejamento (PAT-01a CA 15): sem valor de aquisição nem responsável.</summary>
public sealed record AssetInfo(Guid Id, string Tag, string Name, AssetType Type, Guid? WorkCenterId, AssetStatus Status);

/// <summary>Bens do patrimônio para os outros módulos (ADR 0006, ADR 0038): só a identificação, o centro e a situação.</summary>
public interface IAssets
{
    /// <summary>Por id, inclusive baixados; os excluídos não voltam.</summary>
    Task<IReadOnlyList<AssetInfo>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>Bens não excluídos nem baixados de um centro de trabalho (as máquinas da capacidade, PAT-03b; o baixado sai, PAT-02).</summary>
    Task<IReadOnlyList<AssetInfo>> ByWorkCenterAsync(Guid workCenterId, CancellationToken cancellationToken = default);
}

/// <summary>Tipo do movimento do bem (PAT-02): transferência (local, responsável, centro), baixa e reativação.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AssetMovementKind>))]
public enum AssetMovementKind
{
    Transfer,
    Disposal,
    Reactivation,
}

/// <summary>Motivo da baixa (PAT-02 CA 6): venda, descarte, perda ou roubo, doação e outro.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AssetDisposalReason>))]
public enum AssetDisposalReason
{
    Sale,
    Scrap,
    Loss,
    Donation,
    Other,
}

/// <summary>
/// O bem foi baixado (PAT-02): a Produção e o Planejamento deixam de considerar a máquina pelo <see cref="IAssets"/>. Só ids e a data,
/// sem texto livre (ADR 0037 §2); sem consumidor obrigatório nesta story.
/// </summary>
public sealed record AssetDisposed(Guid AssetId, DateOnly DisposedOn) : BemAqui.SharedKernel.IIntegrationEvent;
