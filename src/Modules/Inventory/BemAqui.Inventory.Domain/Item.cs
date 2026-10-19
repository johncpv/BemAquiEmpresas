using BemAqui.Inventory.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Inventory.Domain;

/// <summary>
/// Item (<c>inventory.items</c>, CAD-02a, R-EST-1): tipo, unidade de estoque, dados fiscais (NCM, CEST, origem), estoque
/// mínimo, lote e série e garantia. O custo médio é do EST-01 e nunca vem da tela.
/// </summary>
public sealed class Item : Entity, IAuditable, ISoftDeletable
{
    private string _code = string.Empty;
    private string _name = string.Empty;

    public ItemType Type { get; set; }

    /// <summary>Código digitado (mantém a <c>ref</c> do Dolibarr), único entre os não excluídos.</summary>
    public string Code
    {
        get => _code;
        set { _code = value; SearchText = BuildSearch(); }
    }

    public string Name
    {
        get => _name;
        set { _name = value; SearchText = BuildSearch(); }
    }

    /// <summary>Código e nome em minúsculas e sem acento, para a busca com pg_trgm (CA 19 e 20).</summary>
    public string SearchText { get; private set; } = string.Empty;

    public Guid UnitId { get; set; }

    public Guid? CategoryId { get; set; }

    /// <summary>Só dígitos (8); obrigatório para item ativo de Matéria-prima, Componente e Produto acabado (CA 7).</summary>
    public string? Ncm { get; set; }

    /// <summary>Só dígitos (7), opcional (CA 8).</summary>
    public string? Cest { get; set; }

    /// <summary>Origem da mercadoria, de 0 a 8 (tabela do ICMS); 0 = Nacional (CA 8).</summary>
    public int Origin { get; set; }

    public decimal MinStock { get; set; }

    public bool TracksLot { get; set; }

    public bool TracksSerial { get; set; }

    /// <summary>Meses de garantia; null = sem garantia (CA 12).</summary>
    public int? WarrantyMonths { get; set; }

    /// <summary>Custo médio (<c>numeric(18,6)</c>), mantido pelo EST-01 (CA 13).</summary>
    public decimal AverageCost { get; private set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Origem da importação (ex.: <c>dolibarr:llx_product:42</c>), gravada só pela MIG-01c.</summary>
    public string? ImportSource { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>Tipos que exigem NCM quando o item está ativo (CA 7).</summary>
    public static bool RequiresNcm(ItemType type) => type is ItemType.RawMaterial or ItemType.Component or ItemType.Finished;

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
    }

    private string BuildSearch() => BemAqui.SharedKernel.SearchText.Normalize(_code + " " + _name);
}
