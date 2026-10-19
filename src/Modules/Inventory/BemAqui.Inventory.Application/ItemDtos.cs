using System.Text.Json.Serialization;
using BemAqui.Inventory.Contracts;

namespace BemAqui.Inventory.Application;

public sealed record UnitInput(string Code, string Name);

public sealed record UnitDetail(Guid Id, string Code, string Name, bool IsActive, string? SeedKey, int ItemCount);

public sealed record CategoryInput(string Name, Guid? ParentId);

/// <summary>Categoria plana; a tela monta a árvore pelo <c>ParentId</c> (CA 3).</summary>
public sealed record CategoryDetail(Guid Id, string Name, Guid? ParentId, bool IsActive, int ItemCount, int ChildCount, string? ImportSource = null);

public sealed record WarrantyDefaultDetail(Guid Id, ItemType ItemType, int? Months);

public sealed record WarrantyDefaultInput(int? Months);

/// <summary>
/// Criação e edição do item (CA 6 a 12). Custo médio não entra (CA 13): membro desconhecido no corpo responde 400.
/// No Serviço, estoque mínimo, lote e série são ignorados (CA 9).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ItemInput(
    ItemType Type,
    string Code,
    string Name,
    Guid UnitId,
    Guid? CategoryId,
    string? Ncm,
    string? Cest,
    int Origin,
    decimal MinStock,
    bool TracksLot,
    bool TracksSerial,
    int? WarrantyMonths);

public sealed record ItemDetail(
    Guid Id,
    ItemType Type,
    string Code,
    string Name,
    Guid UnitId,
    string UnitCode,
    Guid? CategoryId,
    string? CategoryName,
    string? Ncm,
    string? Cest,
    int Origin,
    decimal MinStock,
    bool TracksLot,
    bool TracksSerial,
    int? WarrantyMonths,
    decimal AverageCost,
    bool IsActive,
    string? ImportSource,
    DateTimeOffset CreatedAt);

public sealed record ItemListItem(Guid Id, string Code, string Name, ItemType Type, string? CategoryName, string UnitCode, string? Ncm, bool TracksLot, bool TracksSerial, decimal MinStock, bool IsActive);

public sealed record ItemListQuery(string? Q, ItemType? Type, Guid? CategoryId, bool IncludeInactive);

/// <summary>Resultado da busca da topbar (CA 20).</summary>
public sealed record ItemSearchHit(Guid Id, string Code, string Name, ItemType Type);
