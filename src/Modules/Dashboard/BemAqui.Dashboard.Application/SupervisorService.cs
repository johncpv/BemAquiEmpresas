using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Production.Contracts;

namespace BemAqui.Dashboard.Application;

/// <summary>
/// Painel do supervisor de produção (DSH-03): compõe os blocos pelos contratos do Production (ADR 0028 §1), nunca de tabela. O quadro, a
/// qualidade e as faltas são lidos na hora (tempo real e recarga de 60 s, CA 7); a capacidade e os indicadores passam pelo cache de 60 s do
/// painel (por usuário, versão de permissão, geração e linha) e pelo "Atualizar" (ADR 0028 §3).
/// </summary>
public sealed class SupervisorService(
    DashboardCache cache,
    ICurrentUser currentUser,
    IPermissionService permissions,
    TenantContextHolder tenant,
    IWorkCenters workCenters,
    IProductionBoard board,
    IFactoryLoad factoryLoad,
    IQualityOverview quality,
    IProductionIndicators indicators,
    TimeProvider clock)
{
    private EffectivePermissions? _effective;

    public async Task<SupervisorOverview> OverviewAsync(CancellationToken cancellationToken = default)
    {
        var lines = (await workCenters.ActiveAsync(cancellationToken)).Where(c => !string.IsNullOrWhiteSpace(c.Line)).Select(c => c.Line!.Trim())
            .Distinct(StringComparer.Ordinal).Order(StringComparer.CurrentCulture).ToList();
        var blocks = new List<string> { SupervisorBlocks.Quality };
        if (await CanAsync(SupervisorBlocks.ShortagesPermission, cancellationToken))
        {
            blocks.Add(SupervisorBlocks.Shortages);
        }

        blocks.AddRange([SupervisorBlocks.Board, SupervisorBlocks.Capacity, SupervisorBlocks.Indicators]);
        return new SupervisorOverview(BusinessTime.Today(clock), lines, blocks, await CanAsync(SupervisorBlocks.QualityPermission, cancellationToken),
            await CanAsync(SupervisorBlocks.CreateRequestPermission, cancellationToken), await CanAsync(SupervisorBlocks.ViewPurchasingPermission, cancellationToken));
    }

    /// <summary>Quadro compacto (CA 2), sem cache: o front recarrega pelo aviso do <c>ProductionHub</c>.</summary>
    public async Task<SupervisorBoardBlock> BoardAsync(string? line, CancellationToken cancellationToken = default)
    {
        var s = await board.SummaryAsync(line, cancellationToken);
        return new SupervisorBoardBlock(
            s.OpenCount, s.LateCount, s.AtRiskCount,
            s.Columns.Select(c => new SupervisorBoardColumnItem(c.Key, c.Code, c.Name, c.Count, c.RemainingHours, c.LateCount, c.AtRiskCount)).ToList(),
            s.Highlighted.Take(SupervisorBlocks.BoardCardRows).Select(c => new SupervisorBoardCardItem(
                c.OrderId, c.Number, c.ProductName, c.ProjectId, c.ProjectNumber, c.PartyName, c.ColumnKey, c.StepCode, c.StepName, c.Codes, c.StepNames, c.CurrentStepIndex,
                c.PromisedDate, c.Risk, c.DelayDays)).ToList(),
            clock.GetUtcNow());
    }

    /// <summary>Capacidade (CA 3): <c>IFactoryLoad.WeeklyAsync(5)</c> dos centros da linha; com cache e "Atualizar".</summary>
    public Task<SupervisorCapacityBlock> CapacityAsync(string? line, CancellationToken cancellationToken = default) =>
        CachedAsync("supervisor-capacity", Key(line), async ct =>
        {
            var ids = await CentersOfAsync(line, ct);
            var load = await factoryLoad.WeeklyAsync(SupervisorBlocks.CapacityWeeks, ids, ct);
            static SupervisorCapacityRowItem Row(FactoryLoadRow r) => new(r.WorkCenterId, r.Name, r.Line,
                r.Cells.Select(c => new SupervisorCapacityCellItem(c.WeekStart, c.CommittedHours, c.CapacityHours, c.Percent, c.Band)).ToList(), r.BusyUntil);
            return new SupervisorCapacityBlock(load.Today, load.PlanComputedAt, load.Weeks.Select(w => new SupervisorCapacityWeekItem(w.Start, w.End, w.IsoWeek)).ToList(),
                load.Centers.Select(Row).ToList(), Row(load.Factory), clock.GetUtcNow());
        }, cancellationToken);

    /// <summary>Qualidade (CA 4), sem cache: a fila e as NC recarregam a cada 60 s e ao voltar o foco.</summary>
    public async Task<SupervisorQualityBlock> QualityAsync(string? line, CancellationToken cancellationToken = default)
    {
        var canDecide = await CanAsync(SupervisorBlocks.QualityPermission, cancellationToken);
        var pending = await quality.PendingAsync(line, canDecide, cancellationToken);
        var ncs = await quality.OpenNonConformitiesAsync(line, SupervisorBlocks.NonConformityRows, cancellationToken);
        return new SupervisorQualityBlock(
            canDecide, pending.TotalCount,
            pending.Decidable.Select(i => new SupervisorQualityStepItem(i.StepId, i.OrderId, i.OrderNumber, i.StepCode, i.StepName, i.ProductName, i.ProjectNumber, i.PromisedDate,
                i.Attempt, i.WaitingSince, i.SelfInspection, i.OpenNonConformities)).ToList(),
            ncs.Total,
            ncs.Items.Select(n => new SupervisorNonConformityItem(n.Id, n.Number, n.OrderId, n.OrderNumber, n.StepCode, n.StepName, n.ResponsibleName, n.Status, n.AgeDays)).ToList(),
            clock.GetUtcNow());
    }

    /// <summary>Indicadores (CA 6): os últimos 30 dias, com cache e "Atualizar".</summary>
    public Task<SupervisorIndicatorsBlock> IndicatorsAsync(string? line, CancellationToken cancellationToken = default) =>
        CachedAsync("supervisor-indicators", Key(line), async ct =>
        {
            var today = BusinessTime.Today(clock);
            var s = await indicators.SummaryAsync(today.AddDays(-(SupervisorBlocks.IndicatorDays - 1)), today, line, ct);
            var stoppages = s.Stoppages.Select(x => new SupervisorStoppageItem(x.Reason, SupervisorBlocks.StoppageLabel(x.Reason), x.Hours, x.Pauses,
                x.Orders.Select(o => new SupervisorStoppageOrderItem(o.OrderId, o.OrderNumber, o.Hours)).ToList())).ToList();
            return new SupervisorIndicatorsBlock(s.From, s.To, stoppages.Sum(x => x.Hours), stoppages,
                s.QualityRates.Select(r => new SupervisorQualityRateItem(r.StepCode, r.StepName, r.Decisions, r.Rejections, r.RatePercent)).ToList(), clock.GetUtcNow());
        }, cancellationToken);

    /// <summary>"Atualizar" (CA 7): os próximos pedidos deste usuário ignoram o cache da capacidade e dos indicadores.</summary>
    public void Refresh() => cache.MarkRefresh(TenantId, UserId);

    /// <summary>Centros ativos da linha; sem linha, nulo (todos).</summary>
    private async Task<IReadOnlyCollection<Guid>?> CentersOfAsync(string? line, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var ids = (await workCenters.ActiveAsync(ct)).Where(c => string.Equals(c.Line?.Trim(), line.Trim(), StringComparison.Ordinal)).Select(c => c.Id).ToList();
        // Linha sem centro ativo: nenhum centro (o contrato trata lista vazia como "todos").
        return ids.Count == 0 ? [Guid.Empty] : ids;
    }

    private static string Key(string? line) => "line=" + (line?.Trim() ?? string.Empty);

    private async Task<T> CachedAsync<T>(string card, string parameters, Func<CancellationToken, Task<T>> compute, CancellationToken cancellationToken)
        where T : class
    {
        var scope = new CardScope(TenantId, UserId, (await EffectiveAsync(cancellationToken)).Version);
        var entry = await cache.GetOrComputeAsync(scope, card, parameters, async ct => await compute(ct), cancellationToken);
        return (T)entry.Value;
    }

    /// <summary>A permissão do dono com o módulo dele liberado (CA 9).</summary>
    internal async Task<bool> CanAsync(string permission, CancellationToken cancellationToken) =>
        tenant.Resolved?.IsReleased(CardRule.ModuleOf(permission)) == true && (await EffectiveAsync(cancellationToken)).Has(permission);

    private async Task<EffectivePermissions> EffectiveAsync(CancellationToken cancellationToken) =>
        _effective ??= await permissions.GetAsync(UserId, cancellationToken);

    private Guid TenantId => tenant.Tenant?.Id ?? throw new InvalidOperationException("Painel sem tenant no escopo.");

    private Guid UserId => currentUser.UserId ?? throw new InvalidOperationException("Painel sem usuário autenticado.");
}
