using System.Text.Json.Serialization;
using BemAqui.Assets.Contracts;

namespace BemAqui.Assets.Application;

/// <summary>Como o lote sai (CA 3): uma etiqueta por página no tamanho do parâmetro (impressora de etiquetas) ou a grade na folha A4.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AssetLabelLayout>))]
public enum AssetLabelLayout
{
    Label,
    A4Sheet,
}

/// <summary>
/// "Imprimir etiquetas" (CA 3): os bens escolhidos ou, com <c>AllFromFilter</c>, todos os do filtro da lista (até 500). <c>Filter</c> usa os
/// mesmos campos da lista.
/// </summary>
public sealed record AssetLabelBatchInput(
    IReadOnlyList<Guid>? AssetIds,
    bool AllFromFilter = false,
    AssetLabelFilter? Filter = null,
    AssetLabelLayout Layout = AssetLabelLayout.Label);

/// <summary>Filtro da lista de bens (PAT-01a CA 9) para "todos do filtro".</summary>
public sealed record AssetLabelFilter(string? Q, AssetType? Type, Guid? SiteId, Guid? WorkCenterId, Guid? ResponsibleEmployeeId, AssetStatus? Status);

/// <summary>
/// Lote pedido (CA 3): até 50, <c>AttachmentId</c> do PDF pronto; acima, <c>Queued</c> e o aviso no sino quando pronto. O PDF fica 7 dias.
/// </summary>
public sealed record AssetLabelBatchResult(bool Queued, int Count, Guid? AttachmentId, string Message);

/// <summary>PDF da etiqueta ou do lote.</summary>
public sealed record AssetLabelFile(Stream Content, string FileName);

/// <summary>Tamanho da etiqueta (CA 1), em mm, e o id do parâmetro para o histórico de alterações.</summary>
public sealed record AssetParametersView(int LabelWidthMm, int LabelHeightMm, Guid? LabelSizeId);

public sealed record AssetParametersInput(int LabelWidthMm, int LabelHeightMm);

/// <summary>
/// Ficha resumida do bem pela leitura da etiqueta (CA 5): foto (a regravada sem EXIF/GPS), etiqueta, nome, tipo, local, responsável,
/// centro, situação e a manutenção (próxima prevista e última feita, da PAT-03a; até lá, vazias). Sem valor, série nem nota.
/// </summary>
public sealed record AssetCardView(
    Guid Id,
    string Tag,
    string Name,
    AssetType Type,
    AssetStatus Status,
    string? SiteName,
    string? ResponsibleName,
    bool ResponsibleTerminated,
    string? WorkCenterName,
    Guid? PhotoId,
    DateOnly? NextMaintenanceDate,
    DateOnly? LastMaintenanceDate);
