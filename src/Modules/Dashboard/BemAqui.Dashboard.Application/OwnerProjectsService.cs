using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Finance.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Dashboard.Application;

/// <summary>
/// Cards da DSH-01b no painel do proprietário: Margem (<see cref="IProjectMargins"/>), Projetos em andamento, Carteira e Entregas
/// (<see cref="IProjects"/>, <see cref="IRevenueSummary"/>, <see cref="IReceivables"/>) e Produção (<see cref="IProductionBoard"/>,
/// <see cref="IFactoryLoad"/>). Só compõe (ADR 0028 §1); cada card passa pelo cache de 60 s (§3) e o endpoint exige o módulo e a permissão do
/// dono (§2). A carteira lida do <see cref="IProjects"/> é a mesma para os três cards de projeto (uma leitura por minuto).
/// </summary>
public sealed class OwnerProjectsService(
    DashboardAccess access,
    IProjects projects,
    IProjectMargins margins,
    IRevenueSummary revenue,
    IReceivables receivables,
    IParties parties,
    IProductionBoard board,
    IFactoryLoad factoryLoad,
    TimeProvider clock)
{
    /// <summary>Card Margem (CA 1 e 2): o relatório "Orçado × realizado" com o filtro dos em andamento e o dos entregues ou encerrados no ano.</summary>
    public async Task<Result<MarginOverview>> MarginAsync(CancellationToken cancellationToken = default)
    {
        if (!await access.CanAsync(DashboardCards.CostPermission, cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Custo e margem do projeto são restritos. Fale com o administrador.");
        }

        var today = BusinessTime.Today(clock);
        return await access.CachedAsync(DashboardCards.Margin, "year=" + today.Year, async ct =>
        {
            var active = await margins.SummaryAsync(new ProjectMarginFilter("andamento"), OwnerRules.WorstRows, ct);
            var finished = await margins.PortfolioAsync(FinishedFilter(today.Year), ct);
            if (active.IsFailure || finished.IsFailure)
            {
                throw new InvalidOperationException("O cálculo da margem recusou o pedido: " + (active.Error ?? finished.Error)!.Message);
            }

            var a = active.Value.Portfolio.Figures;
            var f = finished.Value.Figures;
            return new MarginOverview(
                today.Year, active.Value.Portfolio.ProjectCount, a.Revenue, a.ProjectedMarginPct, a.BudgetedMarginPct, a.MarginDeviationPp, finished.Value.ProjectCount,
                f.RealizedMarginPct, f.BudgetedMarginPct, f.RealizedMarginPct is { } r && f.BudgetedMarginPct is { } b ? r - b : null,
                active.Value.Worst.Select(w => new MarginProjectRow(w.Project.Id, w.Project.Number, w.Project.Name, w.Project.PartyName, w.Figures.BudgetedMarginPct,
                    w.Figures.ProjectedMarginPct, w.Figures.MarginDeviationPp, w.WorstCostType)).ToList(),
                clock.GetUtcNow());
        }, cancellationToken);
    }

    /// <summary>Filtro dos encerrados no ano do card Margem: entregues ou encerrados com a entrega (ou o encerramento) no ano.</summary>
    public static ProjectMarginFilter FinishedFilter(int year) => new("concluido", DeliveredFrom: new DateOnly(year, 1, 1), DeliveredTo: new DateOnly(year, 12, 31));

    /// <summary>Card "Projetos em andamento" (CA 1): até 8, atrasados e em risco primeiro, com o nome do cliente só dos mostrados.</summary>
    public Task<ActiveProjectsCard> ActiveProjectsAsync(CancellationToken cancellationToken = default) =>
        access.CachedAsync(DashboardCards.ActiveProjects, string.Empty, async ct =>
        {
            var ordered = OwnerRules.ActiveOrder(await PortfolioAsync(ct));
            var shown = ordered.Take(OwnerRules.ActiveRows).ToList();
            var names = (await parties.FindManyAsync(shown.Select(p => p.PartyId).Distinct().ToList(), ct)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
            return new ActiveProjectsCard(ordered.Count, shown.Select(p => new ActiveProjectItem(
                p.Id, p.Number, p.Name, names.GetValueOrDefault(p.PartyId, string.Empty), p.Status, p.Forecast.PromisedDate, p.Forecast.ForecastDate, p.Forecast.SlackDays,
                p.Forecast.Risk, p.Forecast.DelayDays, p.ContractedAmount)).ToList(), clock.GetUtcNow());
        }, cancellationToken);

    /// <summary>Coluna "Margem" dos projetos do card (com <c>projects.view_cost</c>): orçada → projetada pelo <see cref="IProjectMargins"/>.</summary>
    public async Task<Result<IReadOnlyList<ActiveProjectMarginRow>>> ActiveProjectMarginsAsync(CancellationToken cancellationToken = default)
    {
        var ids = (await ActiveProjectsAsync(cancellationToken)).Items.Select(i => i.Id).ToList();
        var rows = await margins.ForProjectsAsync(ids, cancellationToken);
        if (rows.IsFailure)
        {
            return rows.Error!;
        }

        var byId = rows.Value.ToDictionary(r => r.Project.Id);
        return Result<IReadOnlyList<ActiveProjectMarginRow>>.Success(ids.Where(byId.ContainsKey).Select(id => byId[id])
            .Select(r => new ActiveProjectMarginRow(r.Project.Id, r.Figures.BudgetedMarginPct, r.Figures.ProjectedMarginPct, r.Figures.MarginDeviationPp)).ToList());
    }

    /// <summary>Coluna "Recebido" dos projetos do card (com <c>finance.view</c>): recebido em lote e o % do contratado.</summary>
    public async Task<IReadOnlyList<ActiveProjectReceivedItem>> ActiveProjectsReceivedAsync(CancellationToken cancellationToken = default)
    {
        var items = (await ActiveProjectsAsync(cancellationToken)).Items;
        var received = await receivables.ReceivedByProjectsAsync(items.Select(i => i.Id).ToList(), cancellationToken);
        return items.Select(i =>
        {
            var amount = received.GetValueOrDefault(i.Id);
            return new ActiveProjectReceivedItem(i.Id, amount, OwnerRules.Pct1(amount, i.ContractedAmount));
        }).ToList();
    }

    /// <summary>
    /// Carteira (CA 3): a produzir pelos projetos ainda não fabricados; a faturar (com <c>finance.view</c>) = contratado dos contratos ativos −
    /// receita reconhecida desses projetos pelo <see cref="IRevenueSummary.GrossRevenueByProjectsAsync"/>, da assinatura mais antiga até hoje.
    /// </summary>
    public Task<BacklogCard> BacklogAsync(CancellationToken cancellationToken = default) =>
        access.CachedAsync(DashboardCards.Backlog, string.Empty, async ct =>
        {
            var portfolio = await PortfolioAsync(ct);
            var (toProduce, produceCount) = OwnerRules.ToProduce(portfolio);
            if (!await access.CanAsync("finance.view", ct))
            {
                return new BacklogCard(toProduce, produceCount, null, null, null, null, false, clock.GetUtcNow());
            }

            var active = OwnerRules.ToInvoiceProjects(portfolio);
            var contracted = active.Sum(p => p.ContractedAmount);
            decimal recognized = 0;
            var provisional = false;
            if (active.Count > 0)
            {
                var today = BusinessTime.Today(clock);
                var since = active.Min(p => p.SignedAt);
                var months = await revenue.GrossRevenueByProjectsAsync(active.Select(p => p.Id).ToList(), new DateOnly(since.Year, since.Month, 1), today, ct);
                recognized = months.Sum(m => m.Amount);
                provisional = months.Any(m => m.IsProvisional);
            }

            return new BacklogCard(toProduce, produceCount, contracted, recognized, contracted - recognized, active.Count, provisional, clock.GetUtcNow());
        }, cancellationToken);

    /// <summary>Entregas do mês (CA 1) e o projeto que mais vai atrasar (faixa "Atenção", CA 4).</summary>
    public Task<DeliveriesCard> DeliveriesAsync(CancellationToken cancellationToken = default) =>
        access.CachedAsync(DashboardCards.Deliveries, string.Empty, async ct =>
        {
            var today = BusinessTime.Today(clock);
            var d = OwnerRules.Deliveries(await PortfolioAsync(ct), today);
            return new DeliveriesCard(today.Year, today.Month, d.Forecast, d.Delivered, d.AtRisk, d.Late, d.MostDelayed, clock.GetUtcNow());
        }, cancellationToken);

    /// <summary>Produção (CA 1): projetos em andamento, as contagens do quadro (Kanban) e a linha "Fábrica" da ocupação nas 5 semanas.</summary>
    public Task<ProductionCard> ProductionAsync(CancellationToken cancellationToken = default) =>
        access.CachedAsync(DashboardCards.Production, "weeks=" + OwnerRules.LoadWeeks, async ct =>
        {
            var active = (await PortfolioAsync(ct)).Count(p => OwnerRules.IsActive(p.Status));
            var summary = await board.SummaryAsync(null, ct);
            var load = await factoryLoad.WeeklyAsync(OwnerRules.LoadWeeks, null, ct);
            var cells = load.Factory.Cells.ToDictionary(c => c.WeekStart);
            var weeks = load.Weeks.Select(w =>
            {
                var c = cells.GetValueOrDefault(w.Start);
                return new ProductionWeekItem(w.Start, w.End, w.IsoWeek, c?.CommittedHours ?? 0, c?.CapacityHours ?? 0, c?.Percent, c?.Band ?? FactoryLoadBand.NoCapacity);
            }).ToList();
            return new ProductionCard(active, summary.OpenCount, summary.LateCount, summary.AtRiskCount, weeks, load.Factory.BusyUntil, load.PlanComputedAt, clock.GetUtcNow());
        }, cancellationToken);

    /// <summary>A carteira do <see cref="IProjects"/> com os entregues desde o início do mês, numa leitura por minuto para os cards de projeto.</summary>
    private async Task<IReadOnlyList<ProjectPortfolioItem>> PortfolioAsync(CancellationToken ct)
    {
        var today = BusinessTime.Today(clock);
        var since = new DateOnly(today.Year, today.Month, 1);
        var holder = await access.CachedAsync("projects-portfolio", "since=" + since.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            async inner => new PortfolioHolder(await projects.PortfolioAsync(since, inner)), ct);
        return holder.Items;
    }

    private sealed record PortfolioHolder(IReadOnlyList<ProjectPortfolioItem> Items);
}
