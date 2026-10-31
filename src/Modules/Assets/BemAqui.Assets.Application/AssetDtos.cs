using System.Text.Json.Serialization;
using BemAqui.Assets.Contracts;
using BemAqui.BuildingBlocks.Attachments;

namespace BemAqui.Assets.Application;

/// <summary>Filtros da lista (CA 9): busca por etiqueta, nome e série; tipo, local, centro, responsável e situação.</summary>
public sealed record AssetListQuery(string? Q, AssetType? Type, Guid? SiteId, Guid? WorkCenterId, Guid? ResponsibleEmployeeId, AssetStatus? Status);

/// <summary>
/// Linha da lista (CA 9). <c>AcquisitionValue</c> só vem para quem tem <c>assets.edit</c>: sem a ação, o campo não existe no JSON.
/// <c>PhotoId</c> é a foto limpa mais recente (miniatura). <c>ResponsibleTerminated</c>: o responsável foi desligado (CA 4).
/// </summary>
public sealed record AssetListItem(
    Guid Id,
    string Tag,
    string Name,
    AssetType Type,
    AssetStatus Status,
    string? SerialNumber,
    Guid? SiteId,
    string? SiteName,
    Guid? ResponsibleEmployeeId,
    string? ResponsibleName,
    bool ResponsibleTerminated,
    Guid? WorkCenterId,
    string? WorkCenterName,
    Guid? PhotoId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? AcquisitionValue);

/// <summary>Nota de entrada escolhida (CA 7): número, série, emitente e data.</summary>
public sealed record AssetInvoiceView(Guid Id, string Number, string Series, string IssuerName, DateOnly IssueDate);

/// <summary>
/// Ficha do bem (CA 2, 4, 9 e 10). Valor de aquisição só com <c>assets.edit</c> (fora do JSON sem a ação). <c>Version</c> é o
/// <c>xmin</c> para a gravação (409 <c>concurrency_conflict</c>).
/// </summary>
public sealed record AssetDetail(
    Guid Id,
    string Tag,
    string Name,
    AssetType Type,
    AssetStatus Status,
    string? Brand,
    string? Model,
    string? SerialNumber,
    DateOnly? AcquisitionDate,
    Guid? IncomingInvoiceId,
    AssetInvoiceView? IncomingInvoice,
    string? InvoiceRef,
    Guid? SupplierPartyId,
    string? SupplierName,
    Guid? SiteId,
    string? SiteName,
    Guid? ResponsibleEmployeeId,
    string? ResponsibleName,
    bool ResponsibleTerminated,
    Guid? WorkCenterId,
    string? WorkCenterName,
    string? Notes,
    Guid? PhotoId,
    DateTimeOffset CreatedAt,
    uint Version,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? AcquisitionValue);

/// <summary>
/// Dados do bem (CA 2). Obrigatórios: nome e tipo. O valor de aquisição só com <c>assets.edit</c>. Com a nota do sistema
/// (<c>IncomingInvoiceId</c>), fornecedor e data de aquisição vêm dela quando não informados e a referência em texto sai.
/// <c>Version</c>: na edição, a versão lida.
/// </summary>
public sealed record AssetInput(
    string? Name,
    AssetType? Type,
    string? Brand = null,
    string? Model = null,
    string? SerialNumber = null,
    DateOnly? AcquisitionDate = null,
    decimal? AcquisitionValue = null,
    Guid? IncomingInvoiceId = null,
    string? InvoiceRef = null,
    Guid? SupplierPartyId = null,
    Guid? SiteId = null,
    Guid? ResponsibleEmployeeId = null,
    Guid? WorkCenterId = null,
    string? Notes = null,
    uint? Version = null);

/// <summary>Sugestão do centro (CA 3): local do último bem do centro e o responsável mais comum (ativo).</summary>
public sealed record AssetSuggestionView(Guid? SiteId, string? SiteName, Guid? ResponsibleEmployeeId, string? ResponsibleName);

/// <summary>Responsável no seletor (CA 4): só ativos, sem dado sensível (id, nome, matrícula e cargo).</summary>
public sealed record AssetResponsibleChoice(Guid Id, string Name, string? Registration, string PositionName);

/// <summary>Centro de trabalho no seletor (só ativos).</summary>
public sealed record AssetWorkCenterChoice(Guid Id, string Name, string? Line);

/// <summary>Fornecedor no seletor (CA 7, papel Fornecedor do <c>IParties</c>).</summary>
public sealed record AssetSupplierChoice(Guid Id, string Name, string? Document);

/// <summary>Nota de entrada no seletor (CA 7): fornecedor e data de emissão para preencher o bem.</summary>
public sealed record AssetInvoiceChoice(Guid Id, string Number, string Series, Guid? IssuerPartyId, string IssuerName, DateOnly IssueDate);

/// <summary>Foto e documentos do bem (CA 8): a foto vale só depois do antivírus (<c>Clean</c>).</summary>
public sealed record AssetFiles(Guid? PhotoId, IReadOnlyList<AttachmentDto> Photos, IReadOnlyList<AttachmentDto> Documents);

/// <summary>Local do bem (CA 5) com a quantidade de bens nele.</summary>
public sealed record AssetSiteView(Guid Id, string Name, bool IsActive, bool IsSeed, int Assets);

/// <summary>Criar ou renomear o local; <c>IsActive</c> só na edição.</summary>
public sealed record AssetSiteInput(string? Name, bool? IsActive = null);
