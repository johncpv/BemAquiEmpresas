using BemAqui.Inventory.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>
/// <see cref="IItemSuggestions"/> (FIS-04a CA 7): itens ativos de estoque (fora Serviço) com o mesmo NCM ou o nome parecido com a descrição
/// da nota (<c>similarity</c> do pg_trgm sobre o <c>search_text</c>, acima de 0,2); o mesmo NCM vem primeiro. Só sugere: quem escolhe é o
/// usuário.
/// </summary>
public sealed class ItemSuggestionsContract(IInventoryDb db) : IItemSuggestions
{
    public const double MinSimilarity = 0.2;

    public async Task<IReadOnlyList<ItemSuggestion>> SuggestAsync(string? ncm, string description, int limit = 3, CancellationToken cancellationToken = default)
    {
        var text = SearchText.Normalize(description);
        var digits = new string((ncm ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        var code = digits.Length == 8 ? digits : null;
        if (text.Length == 0 && code is null)
        {
            return [];
        }

        var take = Math.Clamp(limit, 1, 10);
        var ids = await db.Database.SqlQuery<Guid>($"""
            SELECT i.id AS "Value" FROM inventory.items i
            WHERE i.is_active AND NOT i.is_deleted AND i.type <> 'Service'
              AND ((CAST({code} AS text) IS NOT NULL AND i.ncm = {code}) OR similarity(i.search_text, {text}) > {MinSimilarity})
            ORDER BY (CAST({code} AS text) IS NOT NULL AND i.ncm = {code}) DESC, similarity(i.search_text, {text}) DESC, i.code
            LIMIT {take}
            """).ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await (from i in db.Items.AsNoTracking()
                          where ids.Contains(i.Id)
                          select new { i.Id, i.Code, i.Name, i.UnitId, i.Ncm, Unit = db.Units.IgnoreQueryFilters().Where(u => u.Id == i.UnitId).Select(u => u.Code).FirstOrDefault() })
            .ToListAsync(cancellationToken);
        return ids.Select(id => rows.First(r => r.Id == id))
            .Select(r => new ItemSuggestion(r.Id, r.Code, r.Name, r.UnitId, r.Unit ?? string.Empty, r.Ncm, code is not null && r.Ncm == code)).ToList();
    }
}
