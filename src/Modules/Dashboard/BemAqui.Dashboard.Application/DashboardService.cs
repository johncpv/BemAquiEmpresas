using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Finance.Contracts;
using BemAqui.Production.Contracts;

namespace BemAqui.Dashboard.Application;

/// <summary>
/// Painel do proprietário (DSH-01a): compõe os cards a partir dos contratos dos donos (ADR 0028 §1), nunca de tabela; cada card
/// passa pelo cache de 60 s por usuário, versão de permissão e geração do tenant (§3). Card sem a permissão do dono não é
/// montado (§2): os endpoints exigem a permissão, e o cabeçalho só lista e lê os cards visíveis.
/// </summary>
public sealed class DashboardService(
    DashboardCache cache,
    ICurrentUser currentUser,
    IPermissionService permissions,
    TenantContextHolder tenant,
    IUserDirectory users,
    IBankBalances balances,
    IReceivablesAging aging,
    IRevenueTargets targets,
    IQuoteFunnel funnel,
    IBusinessCalendar calendar,
    ICashForecastSource cashForecast,
    IRevenueSource revenue,
    IDecisionAgenda agenda,
    TimeProvider clock)
{
    private EffectivePermissions? _effective;

    public async Task<DashboardOverview> OverviewAsync(CancellationToken cancellationToken = default)
    {
        var visible = await VisibleCardsAsync(cancellationToken);
        bool Sees(string card) => visible.Contains(card, StringComparer.Ordinal);
        var cash = Sees(DashboardCards.CashForecast) ? await CashForecastAsync(cancellationToken) : null;
        var bank = Sees(DashboardCards.Balances) ? await BalancesAsync(cancellationToken) : null;
        var decisions = Sees(DashboardCards.Decisions) ? await DecisionsAsync(cancellationToken) : null;
        var overdue = Sees(DashboardCards.Overdue) ? await OverdueAsync(cancellationToken) : null;
        var now = BusinessTime.ToBusiness(clock.GetUtcNow());
        var today = DateOnly.FromDateTime(now.DateTime);
        var name = await users.DisplayNameAsync(UserId, cancellationToken);
        return new DashboardOverview(DashboardRules.Greeting(now.Hour), DashboardRules.FirstName(name), today,
            DashboardRules.Summary(cash, bank, decisions, overdue), DashboardRules.Attention(today, cash, decisions, overdue), visible, clock.GetUtcNow());
    }

    /// <summary>Cards que o usuário vê, na ordem da tela: módulo dono liberado e a permissão dele (CA 4 e 5).</summary>
    public async Task<IReadOnlyList<string>> VisibleCardsAsync(CancellationToken cancellationToken = default)
    {
        var effective = await EffectiveAsync(cancellationToken);
        return DashboardCards.All
            .Where(c => c.Permissions.Any(p => IsReleased(CardRule.ModuleOf(p)) && effective.Has(p)))
            .Select(c => c.Code).ToList();
    }

    public Task<BalancesCard> BalancesAsync(CancellationToken cancellationToken = default) =>
        CachedAsync(DashboardCards.Balances, string.Empty, async ct =>
        {
            var summary = await balances.CurrentAsync(ct);
            return new BalancesCard(summary.Date, summary.Consolidated, summary.Accounts.Select(a => new BalanceAccountItem(a.Id, a.Name, a.Balance)).ToList(),
                summary.Accounts.Any(a => a.OpeningMissing), clock.GetUtcNow());
        }, cancellationToken);

    public Task<CashForecastCard> CashForecastAsync(CancellationToken cancellationToken = default) =>
        CachedAsync(DashboardCards.CashForecast, "weeks=" + DashboardCards.ForecastWeeks, async ct =>
        {
            var forecast = await cashForecast.SummaryAsync(DashboardCards.ForecastWeeks, ct);
            if (forecast is null)
            {
                return new CashForecastCard(false, null, [], [], null, null, null, null, clock.GetUtcNow());
            }

            return new CashForecastCard(true, forecast.CurrentBalance,
                forecast.Weeks.Select(w => new CashWeekItem(w.WeekEnd, w.BaseBalance, w.AdjustedBalance)).ToList(),
                forecast.Horizons.Select(h => new CashHorizonItem(h.Days, h.BaseBalance, h.AdjustedBalance)).ToList(),
                new CashPointItem(forecast.LowestBase.Date, forecast.LowestBase.Balance), new CashPointItem(forecast.LowestAdjusted.Date, forecast.LowestAdjusted.Balance),
                forecast.LowestBase.Balance - forecast.LowestAdjusted.Balance,
                forecast.FirstAdjustedNegative is { } n ? new CashPointItem(n.Date, n.Balance) : null, clock.GetUtcNow());
        }, cancellationToken);

    public Task<RevenueCard> RevenueAsync(CancellationToken cancellationToken = default) =>
        CachedAsync(DashboardCards.Revenue, string.Empty, async ct =>
        {
            var today = BusinessTime.Today(clock);
            var year = await targets.ForYearAsync(today.Year, ct);
            var sales = await revenue.YearAsync(today.Year, ct);
            var monthTarget = year.Months.FirstOrDefault(m => m.Month == today.Month)?.Amount;
            var toDate = sales?.Months.Where(m => m.Month <= today.Month).ToList();
            decimal? monthAmount = toDate is null ? null : toDate.Where(m => m.Month == today.Month).Sum(m => m.Amount);
            decimal? yearAmount = toDate?.Sum(m => m.Amount);
            var endOfMonth = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
            var left = await calendar.WorkingDaysAsync(today, endOfMonth, ct);
            var canEdit = IsReleased("settings") && (await EffectiveAsync(ct)).Has("settings.edit");
            return new RevenueCard(today.Year, today.Month, sales is not null, monthAmount, yearAmount, monthTarget, year.YearTotal,
                DashboardRules.Pct(monthAmount, monthTarget), DashboardRules.Pct(yearAmount, year.YearTotal), toDate?.Any(m => m.Provisional) ?? false, left, canEdit,
                clock.GetUtcNow());
        }, cancellationToken);

    public Task<OverdueCard> OverdueAsync(CancellationToken cancellationToken = default) =>
        CachedAsync(DashboardCards.Overdue, "top=5", async ct =>
        {
            var s = await aging.SummaryAsync(5, ct);
            return new OverdueCard(s.Today, s.Total, s.Count, s.Customers,
                s.Buckets.Select(b => new OverdueBucketItem(b.Label, b.FromDays, b.ToDays, b.Total, b.Count)).ToList(),
                s.TopCustomers.Select(c => new OverdueCustomerItem(c.PartyId, c.PartyName, c.Total, c.MaxDaysLate, c.AvgDelayDays)).ToList(), s.MaxDaysLate,
                s.Largest is { } l ? new OverdueLargestItem(l.ReceivableId, l.Number, l.PartyId, l.PartyName, l.Balance, l.DaysLate, l.CustomerAvgDelayDays) : null,
                clock.GetUtcNow());
        }, cancellationToken);

    public Task<CommercialCard> CommercialAsync(CancellationToken cancellationToken = default) =>
        CachedAsync(DashboardCards.Commercial, string.Empty, async ct =>
        {
            var s = await funnel.SummaryAsync(ct);
            return new CommercialCard(s.From, s.To, s.OpenCount, s.OpenAmount, s.ConversionByCount, s.ConversionByAmount, s.Decided, s.ExpiringCount, s.ExpiringAmount,
                s.Stages.Select(x => new FunnelStageItem(x.Status, x.Situation, x.Count, x.Amount)).ToList(), clock.GetUtcNow());
        }, cancellationToken);

    public Task<DecisionsCard> DecisionsAsync(CancellationToken cancellationToken = default) =>
        CachedAsync(DashboardCards.Decisions, string.Empty, async ct =>
        {
            var items = await agenda.PendingForAsync(UserId, ct);
            if (items is null)
            {
                return new DecisionsCard(false, 0, 0, null, [], clock.GetUtcNow());
            }

            return new DecisionsCard(true, items.Count, items.Sum(i => i.Amount ?? 0), items.Where(i => i.DueDate is not null).Min(i => i.DueDate), items, clock.GetUtcNow());
        }, cancellationToken);

    /// <summary>"Atualizar" (CA 13): os próximos pedidos deste usuário ignoram o que está no cache.</summary>
    public void Refresh() => cache.MarkRefresh(TenantId, UserId);

    private async Task<T> CachedAsync<T>(string card, string parameters, Func<CancellationToken, Task<T>> compute, CancellationToken cancellationToken)
        where T : class
    {
        var scope = new CardScope(TenantId, UserId, (await EffectiveAsync(cancellationToken)).Version);
        var entry = await cache.GetOrComputeAsync(scope, card, parameters, async ct => await compute(ct), cancellationToken);
        return (T)entry.Value;
    }

    private async Task<EffectivePermissions> EffectiveAsync(CancellationToken cancellationToken) =>
        _effective ??= await permissions.GetAsync(UserId, cancellationToken);

    private bool IsReleased(string module) => tenant.Resolved?.IsReleased(module) == true;

    private Guid TenantId => tenant.Tenant?.Id ?? throw new InvalidOperationException("Painel sem tenant no escopo.");

    private Guid UserId => currentUser.UserId ?? throw new InvalidOperationException("Painel sem usuário autenticado.");
}
