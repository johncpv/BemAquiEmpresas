namespace BemAqui.Commercial.Contracts;

/// <summary>Situação no funil (COM-08), com o parâmetro <see cref="Situation"/> da lista de orçamentos para o drill-down.</summary>
public sealed record QuoteFunnelStageSummary(string Status, string Situation, int Count, decimal Amount);

/// <summary>
/// Resumo do funil dos últimos 90 dias para o painel do proprietário (DSH-01a CA 5): o mesmo cálculo da tela Funil, só com
/// preço (nunca custo ou margem, D7). "Em aberto" são os rascunhos e os enviados, ainda sem decisão; "Vencendo" são os enviados
/// que vencem em até 7 dias (todos os autores).
/// </summary>
public sealed record QuoteFunnelSummary(
    DateOnly From, DateOnly To, int OpenCount, decimal OpenAmount, decimal? ConversionByCount, decimal? ConversionByAmount, int Decided,
    int ExpiringCount, decimal ExpiringAmount, IReadOnlyList<QuoteFunnelStageSummary> Stages);

public interface IQuoteFunnel
{
    Task<QuoteFunnelSummary> SummaryAsync(CancellationToken cancellationToken = default);
}
