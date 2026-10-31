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

    /// <summary>Bens não excluídos de um centro de trabalho (as máquinas da capacidade, PAT-03b).</summary>
    Task<IReadOnlyList<AssetInfo>> ByWorkCenterAsync(Guid workCenterId, CancellationToken cancellationToken = default);
}
