using BemAqui.Engineering.Domain;
using BemAqui.Inventory.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Engineering.Application;

/// <summary>Linha já aparada e validada sem banco (CA 2, 3): a posição na lista é a sequência 1..n.</summary>
public sealed record NormalizedBomLine(Guid? Id, int Sequence, Guid ItemId, decimal Quantity, Guid? UnitId, decimal ScrapPct, string? Notes);

/// <summary>Diferença de um item entre a revisão liberada e a nova (CA 13): antes → depois, a reservar a mais e a liberar.</summary>
public sealed record BomItemDiff(Guid ItemId, decimal Before, decimal After, BomChange Change)
{
    public decimal MoreToReserve => Math.Max(0, After - Before);

    public decimal ToRelease => Math.Max(0, Before - After);
}

/// <summary>Mudança do item entre duas revisões (comparação lado a lado, CA 15, e diálogo da revisão, CA 13).</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<BomChange>))]
public enum BomChange
{
    Unchanged,
    Added,
    Removed,
    Changed,
}

/// <summary>
/// Regras da BOM sem banco (ENG-02): limites da linha, item sem repetir, a necessidade da OS (quantidade por unidade × quantidade da OS ×
/// (1 + perda %), arredondada em 4 casas no fim), a diferença por item entre revisões e a mensagem da OS que não muda mais (CA 11).
/// </summary>
public static class BomRules
{
    public const int MaxLines = 200;
    public const int MaxNotes = 200;
    public const decimal MaxQuantity = 999_999.9999m;
    public const decimal MaxScrapPct = 100m;

    /// <summary>Teto da necessidade (cabe em <c>numeric(18,4)</c> com folga).</summary>
    public const decimal MaxRequired = 9_999_999_999m;

    public static (IReadOnlyList<NormalizedBomLine> Lines, Dictionary<string, string[]> Errors) Validate(IReadOnlyList<BomLineInput>? lines)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Add(string field, string message) => errors.TryAdd(field, [message]);
        var list = lines ?? [];
        if (list.Count > MaxLines)
        {
            Add("lines", $"Inclua no máximo {MaxLines} linhas na BOM.");
        }

        var normalized = new List<NormalizedBomLine>();
        var seen = new Dictionary<Guid, int>();
        for (var i = 0; i < list.Count && i < MaxLines; i++)
        {
            var line = list[i];
            var field = $"lines[{i}]";
            if (line.ItemId == Guid.Empty)
            {
                Add(field + ".itemId", "Escolha o item.");
            }
            else if (seen.TryGetValue(line.ItemId, out var first))
            {
                Add(field + ".itemId", $"O item já está na linha {first + 1}.");
            }
            else
            {
                seen[line.ItemId] = i;
            }

            if (line.Quantity <= 0 || line.Quantity > MaxQuantity || decimal.Round(line.Quantity, 4) != line.Quantity)
            {
                Add(field + ".quantity", "Informe a quantidade por unidade do produto, maior que zero, com até 4 casas.");
            }

            if (line.ScrapPct < 0 || line.ScrapPct > MaxScrapPct || decimal.Round(line.ScrapPct, 4) != line.ScrapPct)
            {
                Add(field + ".scrapPct", "Informe a perda entre 0 e 100%.");
            }

            var notes = string.IsNullOrWhiteSpace(line.Notes) ? null : line.Notes.Trim();
            if (notes is { Length: > MaxNotes })
            {
                Add(field + ".notes", $"A observação tem até {MaxNotes} caracteres.");
            }

            normalized.Add(new NormalizedBomLine(line.Id, i + 1, line.ItemId, line.Quantity, line.UnitId, line.ScrapPct, notes));
        }

        return (normalized, errors);
    }

    /// <summary>Necessidade da OS na unidade da linha, sem arredondar: qtd × qtd da OS × (1 + perda %).</summary>
    public static decimal RawRequirement(decimal quantityPerUnit, decimal orderQuantity, decimal scrapPct) =>
        quantityPerUnit * orderQuantity * (1m + (scrapPct / 100m));

    /// <summary>Arredondamento do fim (4 casas, <c>AwayFromZero</c>).</summary>
    public static decimal Round(decimal quantity) => Math.Round(quantity, 4, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Necessidade da OS na unidade de estoque (CA 3): na unidade de estoque só arredonda; noutra, o <see cref="IUnitConversion"/> converte a
    /// quantidade crua e arredonda uma vez, no fim. Sem conversão, o erro <c>unit_conversion_missing</c> do Estoque.
    /// </summary>
    public static async Task<Result<decimal>> RequirementAsync(IUnitConversion conversion, Guid itemId, decimal quantityPerUnit, decimal orderQuantity, decimal scrapPct,
        Guid unitId, Guid stockUnitId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conversion);
        var raw = RawRequirement(quantityPerUnit, orderQuantity, scrapPct);
        return unitId == stockUnitId ? Round(raw) : await conversion.ConvertAsync(itemId, raw, unitId, stockUnitId, ct);
    }

    /// <summary>Falta da linha: o que a necessidade passa do disponível (0 quando cobre).</summary>
    public static decimal Shortage(decimal required, decimal available) => Math.Max(0, required - Math.Max(0, available));

    /// <summary>Diferença por item entre a revisão anterior e a nova, na unidade de estoque (CA 13, 15).</summary>
    public static IReadOnlyList<BomItemDiff> Diff(IEnumerable<(Guid ItemId, decimal Required)> before, IEnumerable<(Guid ItemId, decimal Required)> after)
    {
        var old = before.GroupBy(l => l.ItemId).ToDictionary(g => g.Key, g => g.Sum(l => l.Required));
        var now = after.GroupBy(l => l.ItemId).ToDictionary(g => g.Key, g => g.Sum(l => l.Required));
        var diffs = new List<BomItemDiff>();
        foreach (var (item, required) in now)
        {
            diffs.Add(old.TryGetValue(item, out var previous)
                ? new BomItemDiff(item, previous, required, previous == required ? BomChange.Unchanged : BomChange.Changed)
                : new BomItemDiff(item, 0, required, BomChange.Added));
        }

        diffs.AddRange(old.Where(o => !now.ContainsKey(o.Key)).Select(o => new BomItemDiff(o.Key, o.Value, 0, BomChange.Removed)));
        return diffs;
    }

    /// <summary>Aviso do CA 14: a revisão pede menos do que já foi baixado; a devolução é pela requisição (EST-03). Sem bloquear.</summary>
    public static string? IssuedAboveWarning(decimal issued, decimal required, string itemName, string unitCode) =>
        issued > required
            ? $"Já foram baixados {Format(issued)} {unitCode} de {itemName}; a nova necessidade é {Format(required)} {unitCode}. Devolva a diferença ao estoque pela requisição."
            : null;

    /// <summary>Só a OS que ainda produz muda a BOM (CA 11): concluída ou cancelada responde 409.</summary>
    public static Error? Closed(ProductionOrderInfo order)
    {
        ArgumentNullException.ThrowIfNull(order);
        var situation = order.Status switch
        {
            ProductionOrderState.Cancelled => "cancelada",
            ProductionOrderState.Completed => "concluída",
            _ => null,
        };
        return situation is null ? null : Error.Conflict("production_order_closed", $"A OS {order.Number} está {situation}: a BOM não muda.");
    }

    /// <summary>O item pode entrar na BOM (CA 2): existe, não é Serviço nem o produto da OS; item novo na BOM precisa estar ativo.</summary>
    public static string? ItemProblem(ItemSummary? item, Guid? productId, bool alreadyInBom)
    {
        if (item is null || item.IsDeleted)
        {
            return "Escolha um item do estoque.";
        }

        if (item.Type == ItemType.Service)
        {
            return "Serviço não entra na BOM. Escolha um material.";
        }

        if (productId == item.Id)
        {
            return "O produto da OS não entra na própria BOM.";
        }

        return !item.IsActive && !alreadyInBom ? "Escolha um item ativo do estoque." : null;
    }

    public static string Format(decimal quantity) =>
        Round(quantity).ToString("#,##0.####", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

    public static Error? ToError(Dictionary<string, string[]> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return errors.Count == 0 ? null : Error.Validation("validation", errors.First().Value[0], errors);
    }

    public static Error Invalid(string field, string message) =>
        Error.Validation("validation", message, new Dictionary<string, string[]> { [field] = [message] });

    public static Error NotFound() => Error.NotFound("bom_not_found", "BOM não encontrada.");

    public static Error NotDraft() => Error.Conflict("bom_not_draft", "A BOM liberada não muda. Para mudar, revise a BOM.");

    public static Error Stale() => Error.Conflict("concurrency_conflict", "A BOM mudou desde que você abriu. Recarregue.");

    /// <summary>Situação em português para a lista de revisões (CA 15).</summary>
    public static string Label(BomStatus status) => status switch
    {
        BomStatus.Draft => "Rascunho",
        BomStatus.Released => "Liberada",
        _ => "Substituída",
    };
}
