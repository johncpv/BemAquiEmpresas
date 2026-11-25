using BemAqui.BuildingBlocks.Time;
using BemAqui.Pos.Contracts;
using BemAqui.Sales.Contracts;

namespace BemAqui.Dashboard.Application;

/// <summary>
/// Caixa aberto no card "Caixas abertos" (PDV-07 CA 8): loja, caixa, desde quando e há quantos minutos, e o alerta de cancelamentos do turno
/// (PP-22, decidida pelo PO em 24/11/2026): o valor das vendas canceladas na sessão passou do percentual da loja (padrão 3%) das vendas do turno.
/// </summary>
public sealed record OpenRegisterItem(
    Guid SessionId, Guid StoreId, string StoreName, Guid RegisterId, int RegisterNumber, DateTimeOffset OpenedAt, int OpenMinutes, int Sales, int Cancelled,
    decimal CancelledPercent, decimal CancelAlertPercent, bool CancelAlert);

/// <summary>
/// Cards da loja no Início (PDV-07 CA 8 a 10): "Vendas de hoje" (valor e quantidade, com o mesmo dia da semana anterior), "Caixas abertos" e "Ticket
/// médio" (hoje e últimos 7 dias). <c>MaybeUnsent</c>: algum caixa aberto está sem contato há mais de <see cref="StoreSalesService.UnsentAfter"/>
/// (pode haver vendas na fila do aparelho). Valores são o total das vendas feitas (sem as canceladas), pela data de negócio em São Paulo.
/// </summary>
public sealed record StoreSalesCard(
    DateOnly Today, int TodayCount, decimal TodayAmount, int LastWeekCount, decimal LastWeekAmount, decimal? ChangePct, decimal? TicketToday, decimal? TicketWeek,
    int WeekCount, IReadOnlyList<OpenRegisterItem> OpenRegisters, bool MaybeUnsent, DateTimeOffset UpdatedAt);

/// <summary>
/// Cards da loja no Início (PDV-07), pelos contratos dos donos (ADR 0028 §1): a receita do dia pelo <see cref="ISalesRevenue"/>, as sessões abertas
/// pelo <see cref="IPosBackoffice"/> e as vendas e cancelamentos de cada sessão pelo <see cref="ICashSessionSales"/>. Nenhuma tabela do <c>Sales</c>
/// nem do <c>Pos</c> é lida aqui. Cache de 60 s por usuário (ADR 0028 §3).
/// </summary>
public sealed class StoreSalesService(DashboardAccess access, ISalesRevenue revenue, IPosBackoffice pos, ICashSessionSales sessions, TimeProvider clock)
{
    public const string Card = "store-sales";

    /// <summary>Caixa aberto sem contato há mais que isto (o aparelho conversa a cada 1 a 5 min com rede): pode ter vendas na fila.</summary>
    public static readonly TimeSpan UnsentAfter = TimeSpan.FromMinutes(10);

    public Task<StoreSalesCard> GetAsync(CancellationToken cancellationToken = default) =>
        access.CachedAsync(Card, string.Empty, ComputeAsync, cancellationToken);

    private async Task<StoreSalesCard> ComputeAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var today = BusinessTime.Today(clock);
        var lines = await revenue.RevenueAsync(today.AddDays(-7), today, cancellationToken);
        (int Count, decimal Amount) Day(DateOnly d) => (lines.Where(l => l.Date == d).Sum(l => l.Sales), lines.Where(l => l.Date == d).Sum(l => l.Gross - l.Discounts + l.ServiceFees));
        var (todayCount, todayAmount) = Day(today);
        var (lastCount, lastAmount) = Day(today.AddDays(-7));
        var week = lines.Where(l => l.Date > today.AddDays(-7)).ToList();
        var weekCount = week.Sum(l => l.Sales);
        var weekAmount = week.Sum(l => l.Gross - l.Discounts + l.ServiceFees);

        var open = await pos.OpenSessionsAsync(cancellationToken);
        var registers = new List<OpenRegisterItem>();
        foreach (var s in open)
        {
            var summary = await sessions.SummaryAsync(s.SessionId, [], cancellationToken);
            var pct = StoreSalesRules.CancelledPercent(summary.CancelledTotal, summary.Total);
            registers.Add(new OpenRegisterItem(s.SessionId, s.StoreId, s.StoreName, s.RegisterId, s.RegisterNumber, s.OpenedAt, (int)Math.Max(0, (now - s.OpenedAt).TotalMinutes),
                summary.Sales, summary.Cancelled, pct, s.CancelAlertPercent, StoreSalesRules.CancelAlert(pct, s.CancelAlertPercent)));
        }

        var unsent = open.Any(s => s.LastContactAt is not { } last || now - last > UnsentAfter);
        return new StoreSalesCard(today, todayCount, todayAmount, lastCount, lastAmount, DashboardRules.Pct(todayAmount - lastAmount, lastAmount == 0 ? null : lastAmount),
            StoreSalesRules.Ticket(todayAmount, todayCount), StoreSalesRules.Ticket(weekAmount, weekCount), weekCount, registers, unsent, now);
    }
}

/// <summary>Regras dos cards da loja (PDV-07), puras para o teste de unidade.</summary>
public static class StoreSalesRules
{
    /// <summary>Cancelado ÷ (feito + cancelado) do turno, em %, com 2 casas; zero sem vendas.</summary>
    public static decimal CancelledPercent(decimal cancelledTotal, decimal doneTotal) =>
        cancelledTotal + doneTotal <= 0 ? 0m : decimal.Round(cancelledTotal / (cancelledTotal + doneTotal) * 100m, 2, MidpointRounding.AwayFromZero);

    /// <summary>PP-22: alerta quando passa (estritamente) do percentual da loja; zero desliga.</summary>
    public static bool CancelAlert(decimal cancelledPercent, decimal alertPercent) => alertPercent > 0 && cancelledPercent > alertPercent;

    /// <summary>Ticket médio: total ÷ quantidade, com 2 casas; sem venda, nulo.</summary>
    public static decimal? Ticket(decimal amount, int count) => count == 0 ? null : decimal.Round(amount / count, 2, MidpointRounding.AwayFromZero);
}
