namespace BemAqui.BuildingBlocks.Paging;

/// <summary>Parâmetros de paginação vindos da query string (<c>page</c>, <c>pageSize</c>, <c>sort</c>).</summary>
public sealed record PageRequest(int Page = 1, int PageSize = PageRequest.DefaultPageSize, string? Sort = null)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Página mínima 1; tamanho entre 1 e <paramref name="maxPageSize"/> (acima é limitado ao máximo).</summary>
    public PageRequest Normalize(int maxPageSize = MaxPageSize) => this with
    {
        Page = Math.Max(1, Page),
        PageSize = PageSize <= 0 ? DefaultPageSize : Math.Min(PageSize, maxPageSize),
    };

    public int Skip => (Math.Max(1, Page) - 1) * Math.Max(1, PageSize);

    /// <summary><c>"-createdAt"</c> → (createdAt, desc). Vazio → null.</summary>
    public (string Field, bool Descending)? ParseSort() =>
        string.IsNullOrWhiteSpace(Sort) ? null : (Sort.TrimStart('-', '+'), Sort.StartsWith('-'));
}
