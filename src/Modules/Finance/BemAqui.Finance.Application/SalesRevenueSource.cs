using System.Globalization;
using BemAqui.Sales.Contracts;

namespace BemAqui.Finance.Application;

/// <summary>
/// Receita das vendas no DRE por competência (VND-01 CA 12): o líquido das vendas concluídas (preço − desconto + taxa de serviço) por dia, loja e
/// canal, lido do <see cref="ISalesRevenue"/>, na conta de receita das vendas (<see cref="SaleReceivableWriter.RevenueAccountAsync"/>). O
/// recebível de venda fica fora da receita pelo título (ele vale o líquido da adquirente). Sem projeto: com filtro de projeto, nada entra.
/// Sem vendas (a fábrica), não muda nada no DRE.
/// </summary>
public sealed class SalesRevenueSource(IEnumerable<ISalesRevenue> sources, SaleReceivableWriter writer)
{
    public const string DocumentKind = "Sales";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<IReadOnlyList<RevenueAmount>> AmountsAsync(DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? projectIds, CancellationToken cancellationToken = default)
    {
        if (projectIds is not null)
        {
            return [];
        }

        var (account, lines) = await LinesAsync(from, to, cancellationToken);
        return account is { } id
            ? lines.GroupBy(l => l.Date).Select(g => new RevenueAmount(id, null, g.Key, g.Sum(l => l.Net))).Where(a => a.Amount != 0).ToList()
            : [];
    }

    /// <summary>Uma linha por dia, loja e canal no drill-down da célula; a soma é a do <see cref="AmountsAsync"/>.</summary>
    public async Task<IReadOnlyList<RevenueDocument>> DocumentsAsync(IReadOnlyCollection<Guid> accountIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accountIds);
        var (account, lines) = await LinesAsync(from, to, cancellationToken);
        if (account is not { } id || !accountIds.Contains(id))
        {
            return [];
        }

        return lines.Where(l => l.Net != 0).Select(l => new RevenueDocument(DocumentKind, Guid.Empty,
            $"Vendas {SaleReceivablesConsumer.ChannelLabel(l.Channel.ToString())} {l.Date.ToString("dd/MM", PtBr)}", Guid.Empty, id, null, l.Date, l.Net,
            $"{l.Sales} venda(s)", "/app/financeiro/receber?origem=vendas")).ToList();
    }

    private async Task<(Guid? Account, List<SalesRevenueLine> Lines)> LinesAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var lines = new List<SalesRevenueLine>();
        foreach (var source in sources)
        {
            lines.AddRange(await source.RevenueAsync(from, to, ct));
        }

        return lines.Count == 0 ? (null, lines) : (await writer.RevenueAccountAsync(ct), lines);
    }
}
