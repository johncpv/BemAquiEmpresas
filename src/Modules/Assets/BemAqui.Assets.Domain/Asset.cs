using BemAqui.Assets.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Assets.Domain;

/// <summary>
/// Bem do patrimônio (<c>assets.assets</c>, PAT-01a). A etiqueta <c>PAT-00001</c> nasce na criação, é imutável e nunca é
/// reaproveitada (índice único sem filtro: nem a do bem excluído volta). O valor de aquisição só aparece e só muda com
/// <c>assets.edit</c> (ADR 0038 §3). A foto é o anexo <c>AssetPhoto</c> limpo mais recente, como o logo. Sem campos de
/// depreciação (Fase 9).
/// </summary>
[DocumentName("Bem")]
public sealed class Asset : Entity, IAuditable, ISoftDeletable, IConcurrencyVersioned
{
    public string Tag { get; private set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public AssetType Type { get; set; }

    public string? Brand { get; set; }

    public string? Model { get; set; }

    public string? SerialNumber { get; set; }

    public DateOnly? AcquisitionDate { get; set; }

    /// <summary>Valor de aquisição, <c>numeric(18,2)</c>; só com <c>assets.edit</c>.</summary>
    public decimal? AcquisitionValue { get; set; }

    /// <summary>NF de entrada importada pela FIS-04a (<c>IIncomingInvoices</c>).</summary>
    public Guid? IncomingInvoiceId { get; set; }

    /// <summary>Referência da nota em texto livre (número e série) do bem antigo, sem nota no sistema.</summary>
    public string? InvoiceRef { get; set; }

    public Guid? SupplierPartyId { get; set; }

    public Guid? SiteId { get; set; }

    public Guid? ResponsibleEmployeeId { get; set; }

    public Guid? WorkCenterId { get; set; }

    public AssetStatus Status { get; set; } = AssetStatus.Active;

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public uint Version { get; set; }

    /// <summary>A etiqueta é dada uma vez, na criação, e nunca muda.</summary>
    public void AssignTag(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        if (Tag.Length > 0)
        {
            throw new InvalidOperationException("A etiqueta do bem não muda depois da criação.");
        }

        Tag = tag;
    }

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
    }
}

/// <summary>
/// Parâmetro do Patrimônio (<c>assets.parameters</c>, dono Assets, ADR 0016 M6): hoje só o tamanho da etiqueta
/// (<c>assets.label_size</c>, PAT-01b CA 1). Vazio = padrão; editado em Configurações → Parâmetros com <c>settings.edit</c>, auditado.
/// </summary>
public sealed class AssetParameter : Entity, IAuditable
{
    public string Key { get; init; } = string.Empty;

    public string? Value { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Local do bem (<c>assets.sites</c>, ADR 0038 §2): cadastro simples de setores, com seed "Fábrica" e "Escritório" pela
/// <c>seed_key</c> (o tenant renomeia ou exclui; o seed não recria).
/// </summary>
[DocumentName("Local")]
public sealed class AssetSite : Entity, IAuditable, ISoftDeletable
{
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public string? SeedKey { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
    }
}
