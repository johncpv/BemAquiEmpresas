using Microsoft.EntityFrameworkCore;

namespace BemAqui.BuildingBlocks.Paging;

/// <summary>Página de resultados: <c>items</c>, <c>page</c>, <c>pageSize</c> e <c>total</c>.</summary>
public sealed record PagedList<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public static class PagingQueryableExtensions
{
    public static async Task<PagedList<T>> ToPagedListAsync<T>(this IQueryable<T> query, PageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var page = request.Normalize();
        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken);
        return new PagedList<T>(items, page.Page, page.PageSize, total);
    }
}
