using BemAqui.Commercial.Contracts;

namespace BemAqui.Commercial.Application;

/// <summary>Funil para o painel do proprietário (DSH-01a CA 5 e 6): o mesmo <see cref="QuoteService"/> da tela Funil (COM-08).</summary>
public sealed class QuoteFunnelContract(QuoteService quotes) : IQuoteFunnel
{
    public Task<QuoteFunnelSummary> SummaryAsync(CancellationToken cancellationToken = default) => quotes.FunnelSummaryAsync(cancellationToken);
}
