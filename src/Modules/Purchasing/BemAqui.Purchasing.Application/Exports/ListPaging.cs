using BemAqui.BuildingBlocks.Paging;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Purchasing.Application.Exports;

/// <summary>
/// Página da tela ou a lista inteira para exportar (EXP-04, ADR 0045 §3): com <c>max</c>, a mesma consulta sem paginação, lendo as linhas só
/// quando o total cabe no teto (acima dele, o <c>ListExportService</c> recusa com 422 sem montar nada).
/// </summary>
internal static class ListPaging
{
    public static async Task<PagedList<T>> PageOrAllAsync<T>(this IQueryable<T> query, PageRequest page, int? max, CancellationToken cancellationToken)
    {
        if (max is not { } limit)
        {
            return await query.ToPagedListAsync(page, cancellationToken);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = total > limit ? [] : await query.Take(limit).ToListAsync(cancellationToken);
        return new PagedList<T>(items, 1, limit, total);
    }
}
