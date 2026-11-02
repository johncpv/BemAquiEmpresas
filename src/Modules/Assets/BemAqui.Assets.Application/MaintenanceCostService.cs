using System.Globalization;
using System.Text.Json.Serialization;
using BemAqui.Assets.Contracts;
using BemAqui.Assets.Domain;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Assets.Application;

/// <summary>
/// Linha de custo na aba Custo da OS (CA 6 e 7). Mão de obra interna: o nome do executor, o custo-hora e o valor da linha só com
/// <c>hr.view_sensitive</c> (sem a ação, só as horas; o total da mão de obra vem em <see cref="MaintenanceCostsView.Totals"/>).
/// </summary>
public sealed record MaintenanceCostLineView(
    Guid Id,
    MaintenanceCostType CostType,
    string Description,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Amount,
    decimal? Hours,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? HourlyCost,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EmployeeName,
    Guid? PayableId,
    bool Removable,
    DateTimeOffset CreatedAt);

/// <summary>Total por natureza (CA 6): mão de obra interna, prestador, peças e outros.</summary>
public sealed record MaintenanceCostTotal(MaintenanceCostType CostType, decimal Amount);

/// <summary>Aba Custo da OS (CA 6): as linhas, o total por natureza e o total; <c>Version</c> é o <c>xmin</c> da OS.</summary>
public sealed record MaintenanceCostsView(
    Guid OrderId, MaintenanceOrderStatus Status, IReadOnlyList<MaintenanceCostLineView> Lines, IReadOnlyList<MaintenanceCostTotal> Totals, decimal Total, bool LaborDetail, uint Version);

/// <summary>
/// "Lançar custo" (CA 6): mão de obra interna (o executor e as horas; o custo-hora vem do cargo pelo <c>IHourlyCost</c>), peças ou outros
/// (descrição e valor). O prestador vem do título, nunca à mão.
/// </summary>
public sealed record MaintenanceCostInput(MaintenanceCostType? CostType, string? Description = null, decimal? Amount = null, Guid? EmployeeId = null, decimal? Hours = null, uint? Version = null);

/// <summary>Filtros do Patrimônio → Custo de manutenção (CA 7), na URL: de/até (mês, <c>yyyy-MM</c>), bem, centro e tipo.</summary>
public sealed record MaintenanceCostQuery(string? From, string? To, Guid? AssetId, Guid? WorkCenterId, MaintenanceOrderType? Type = null, string? Month = null);

/// <summary>Uma célula do relatório: preventiva, corretiva e o total.</summary>
public sealed record MaintenanceCostCell(decimal Preventive, decimal Corrective, decimal Total, int Orders);

public sealed record MaintenanceCostMonth(string Month, MaintenanceCostCell Cost);

public sealed record MaintenanceCostAssetRow(Guid AssetId, string Tag, string Name, Guid? WorkCenterId, string? WorkCenterName, MaintenanceCostCell Cost);

public sealed record MaintenanceCostCenterRow(Guid? WorkCenterId, string? WorkCenterName, MaintenanceCostCell Cost);

/// <summary>
/// Patrimônio → Custo de manutenção (CA 7): por mês, por bem e por centro, com o total de preventiva × corretiva; o mês é o da conclusão
/// (fuso de São Paulo). "Atualizado às" = <c>ComputedAt</c>.
/// </summary>
public sealed record MaintenanceCostReport(
    string From, string To, IReadOnlyList<MaintenanceCostMonth> Months, IReadOnlyList<MaintenanceCostAssetRow> Assets, IReadOnlyList<MaintenanceCostCenterRow> Centers,
    MaintenanceCostCell Total, DateTimeOffset ComputedAt);

/// <summary>OS do drill-down (CA 7): o total da lista é igual ao da célula.</summary>
public sealed record MaintenanceCostOrderRow(Guid Id, string Number, Guid AssetId, string AssetTag, string AssetName, MaintenanceOrderType Type, DateTimeOffset DoneAt, decimal Cost);

public sealed record MaintenanceCostOrders(IReadOnlyList<MaintenanceCostOrderRow> Orders, decimal Total);

/// <summary>"Custo de manutenção: {mês} · {ano} · total" na ficha do bem (CA 6), só com <c>assets.edit</c>.</summary>
public sealed record AssetMaintenanceCost(decimal Month, decimal Year, decimal Total);

/// <summary>
/// Custo da manutenção (PAT-03b CA 6 e 7, ADR 0038 §3): as linhas por natureza (mão de obra interna pelo custo-hora do cargo do executor, o
/// prestador pelo título confirmado, peças e outros digitados), o <c>cost</c> da OS como a soma (com o <c>xmin</c>) e o relatório por bem,
/// centro e mês. Tudo com <c>assets.edit</c> (não <c>projects.view_cost</c>); nome com custo-hora só com <c>hr.view_sensitive</c>. Nenhum custo
/// de manutenção vai para projeto.
/// </summary>
public sealed class MaintenanceCostService(
    IAssetsDb db, MaintenanceService maintenance, IEmployees employees, IHourlyCost hourlyCosts, IWorkCenters workCenters, IPermissionChecker permissions,
    ICurrentUser currentUser, TimeProvider clock)
{
    public const string NotDoneCode = "maintenance_cost_not_done";
    public const string NoHourlyCostCode = "maintenance_cost_no_hourly_cost";
    public const int MaxMonths = 24;

    public async Task<Result<MaintenanceCostsView>> ListAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await db.MaintenanceOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        return order is null ? NotFound() : await ViewAsync(order, cancellationToken);
    }

    /// <summary>"Lançar custo" (CA 6): só na OS concluída; o <c>cost</c> da OS soma a linha nova na mesma gravação (<c>xmin</c>).</summary>
    public async Task<Result<MaintenanceCostsView>> AddAsync(Guid orderId, MaintenanceCostInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var order = await db.MaintenanceOrders.FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (input.Version is { } version)
        {
            db.Entry(order).Property(o => o.Version).OriginalValue = version;
        }

        if (order.Status != MaintenanceOrderStatus.Done)
        {
            return Error.Conflict(NotDoneCode, "Conclua a OS para lançar o custo.");
        }

        if (input.CostType is not { } type || !Enum.IsDefined(type) || type == MaintenanceCostType.Service)
        {
            return AssetService.Invalid("costType", "Escolha mão de obra interna, peças ou outros. O prestador vem do título a pagar.");
        }

        MaintenanceOrderCost line;
        if (type == MaintenanceCostType.Labor)
        {
            if (input.EmployeeId is not { } employeeId || await employees.FindAsync([employeeId], cancellationToken) is not [var employee, ..])
            {
                return AssetService.Invalid("employeeId", "Escolha o funcionário que executou.");
            }

            if (input.Hours is not { } hours || hours <= 0 || decimal.Round(hours, 4) != hours || hours > MaintenanceRules.LaborHoursMax)
            {
                return AssetService.Invalid("hours", "Informe as horas trabalhadas, maiores que zero.");
            }

            if (await hourlyCosts.ForPositionAsync(employee.PositionId, cancellationToken) is not { } rate || rate <= 0)
            {
                return Error.Validation(NoHourlyCostCode, $"O cargo {employee.PositionName} não tem custo-hora. Cadastre o salário de referência do cargo no RH.",
                    new Dictionary<string, string[]> { ["employeeId"] = ["O cargo do funcionário não tem custo-hora."] });
            }

            line = new MaintenanceOrderCost
            {
                OrderId = orderId, CostType = type, Description = Clean(input.Description) ?? $"Mão de obra interna — {employee.PositionName}", Amount = MaintenanceRules.LaborAmount(hours, rate),
                Hours = hours, HourlyCostSnapshot = rate, EmployeeId = employeeId, CreatedBy = currentUser.UserId, CreatedAt = clock.GetUtcNow(),
            };
        }
        else
        {
            var description = Clean(input.Description);
            if (description is null)
            {
                return AssetService.Invalid("description", type == MaintenanceCostType.Parts ? "Descreva a peça, com até 150 caracteres." : "Descreva o custo, com até 150 caracteres.");
            }

            if (input.Amount is not { } amount || !MaintenanceRules.IsValidMoney(amount))
            {
                return AssetService.Invalid("amount", "Informe um valor maior que zero, em centavos.");
            }

            line = new MaintenanceOrderCost { OrderId = orderId, CostType = type, Description = description, Amount = amount, CreatedBy = currentUser.UserId, CreatedAt = clock.GetUtcNow() };
        }

        db.MaintenanceOrderCosts.Add(line);
        order.Cost = await maintenance.CostSumAsync(orderId, line.Amount, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await ViewAsync(order, cancellationToken);
    }

    /// <summary>Tirar uma linha (CA 6): só a lançada à mão; a do prestador sai pelo título a pagar.</summary>
    public async Task<Result<MaintenanceCostsView>> RemoveAsync(Guid orderId, Guid costId, uint? version, CancellationToken cancellationToken = default)
    {
        var order = await db.MaintenanceOrders.FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        var line = order is null ? null : await db.MaintenanceOrderCosts.FirstOrDefaultAsync(c => c.Id == costId && c.OrderId == orderId, cancellationToken);
        if (order is null || line is null)
        {
            return Error.NotFound("maintenance_cost_not_found", "Custo não encontrado.");
        }

        if (version is { } v)
        {
            db.Entry(order).Property(o => o.Version).OriginalValue = v;
        }

        if (line.PayableId is not null)
        {
            return Error.Conflict("maintenance_cost_from_payable", "O custo do prestador vem do título a pagar. Ajuste no título.");
        }

        db.MaintenanceOrderCosts.Remove(line);
        order.Cost = await maintenance.CostSumAsync(orderId, -line.Amount, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await ViewAsync(order, cancellationToken);
    }

    /// <summary>
    /// Relatório (CA 7): as OS concluídas com custo no período (padrão: os últimos 12 meses), por mês da conclusão, por bem e por centro, com
    /// preventiva × corretiva.
    /// </summary>
    public async Task<Result<MaintenanceCostReport>> ReportAsync(MaintenanceCostQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (Period(query) is not { } period)
        {
            return AssetService.Invalid("from", $"Escolha de 1 a {MaxMonths} meses, no formato aaaa-mm.");
        }

        var rows = await RowsAsync(period.From, period.To, query, null, cancellationToken);
        var centers = await CenterNamesAsync(rows.Select(r => r.WorkCenterId), cancellationToken);
        var months = new List<MaintenanceCostMonth>();
        for (var m = period.From; m <= period.To; m = m.AddMonths(1))
        {
            var key = MonthKey(m);
            months.Add(new MaintenanceCostMonth(key, Cell(rows.Where(r => r.Month == key))));
        }

        var assets = rows.GroupBy(r => r.AssetId).Select(g => new MaintenanceCostAssetRow(g.Key, g.First().Tag, g.First().Name, g.First().WorkCenterId,
            g.First().WorkCenterId is { } c ? centers.GetValueOrDefault(c) : null, Cell(g))).OrderByDescending(a => a.Cost.Total).ThenBy(a => a.Tag, StringComparer.Ordinal).ToList();
        var byCenter = rows.GroupBy(r => r.WorkCenterId).Select(g => new MaintenanceCostCenterRow(g.Key, g.Key is { } c ? centers.GetValueOrDefault(c) : null, Cell(g)))
            .OrderByDescending(c => c.Cost.Total).ToList();
        return new MaintenanceCostReport(MonthKey(period.From), MonthKey(period.To), months, assets, byCenter, Cell(rows), clock.GetUtcNow());
    }

    /// <summary>Drill-down (CA 7): as OS de uma célula (mês, bem, centro, tipo); o total é a soma das mesmas linhas do relatório.</summary>
    public async Task<Result<MaintenanceCostOrders>> OrdersAsync(MaintenanceCostQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (Period(query) is not { } period)
        {
            return AssetService.Invalid("from", $"Escolha de 1 a {MaxMonths} meses, no formato aaaa-mm.");
        }

        var rows = await RowsAsync(period.From, period.To, query, query.Type, cancellationToken);
        if (query.Month is { } month)
        {
            rows = rows.Where(r => r.Month == month).ToList();
        }

        var list = rows.OrderByDescending(r => r.DoneAt).Select(r => new MaintenanceCostOrderRow(r.Id, r.Number, r.AssetId, r.Tag, r.Name, r.Type, r.DoneAt, r.Cost)).ToList();
        return new MaintenanceCostOrders(list, list.Sum(r => r.Cost));
    }

    /// <summary>"Custo de manutenção" na ficha (CA 6): o mês e o ano correntes e o total, só com <c>assets.edit</c> (sem a ação, null).</summary>
    public async Task<AssetMaintenanceCost?> ForAssetAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        if (!await permissions.HasPermissionAsync(AssetService.Module, StandardActions.Edit, cancellationToken))
        {
            return null;
        }

        var done = await db.MaintenanceOrders.AsNoTracking().Where(o => o.AssetId == assetId && o.Status == MaintenanceOrderStatus.Done && o.Cost > 0)
            .Select(o => new { o.DoneAt, o.Cost }).ToListAsync(cancellationToken);
        var today = BusinessTime.Today(clock);
        var dated = done.Select(d => (Date: DateOnly.FromDateTime(BusinessTime.ToBusiness(d.DoneAt!.Value).DateTime), d.Cost)).ToList();
        return new AssetMaintenanceCost(dated.Where(d => d.Date.Year == today.Year && d.Date.Month == today.Month).Sum(d => d.Cost), dated.Where(d => d.Date.Year == today.Year).Sum(d => d.Cost),
            dated.Sum(d => d.Cost));
    }

    private sealed record CostRow(Guid Id, string Number, Guid AssetId, string Tag, string Name, Guid? WorkCenterId, MaintenanceOrderType Type, DateTimeOffset DoneAt, decimal Cost, string Month);

    private async Task<List<CostRow>> RowsAsync(DateOnly fromMonth, DateOnly toMonth, MaintenanceCostQuery query, MaintenanceOrderType? type, CancellationToken ct)
    {
        var start = BusinessTime.StartOfDayUtc(fromMonth);
        var end = BusinessTime.StartOfDayUtc(toMonth.AddMonths(1));
        var rows = from o in db.MaintenanceOrders.AsNoTracking()
                   join a in db.Assets.IgnoreQueryFilters().AsNoTracking() on o.AssetId equals a.Id
                   where o.Status == MaintenanceOrderStatus.Done && o.Cost > 0 && o.DoneAt >= start && o.DoneAt < end
                   select new { o.Id, o.Number, o.AssetId, a.Tag, a.Name, a.WorkCenterId, o.Type, o.DoneAt, o.Cost };
        if (query.AssetId is { } asset)
        {
            rows = rows.Where(r => r.AssetId == asset);
        }

        if (query.WorkCenterId is { } center)
        {
            rows = rows.Where(r => r.WorkCenterId == center);
        }

        if (type is { } t)
        {
            rows = rows.Where(r => r.Type == t);
        }

        return (await rows.Take(20_000).ToListAsync(ct)).Select(r => new CostRow(r.Id, r.Number, r.AssetId, r.Tag, r.Name, r.WorkCenterId, r.Type, r.DoneAt!.Value, r.Cost,
            MonthKey(DateOnly.FromDateTime(BusinessTime.ToBusiness(r.DoneAt!.Value).DateTime)))).ToList();
    }

    private static MaintenanceCostCell Cell(IEnumerable<CostRow> rows)
    {
        var list = rows.ToList();
        var preventive = list.Where(r => r.Type == MaintenanceOrderType.Preventive).Sum(r => r.Cost);
        var corrective = list.Where(r => r.Type == MaintenanceOrderType.Corrective).Sum(r => r.Cost);
        return new MaintenanceCostCell(preventive, corrective, preventive + corrective, list.Count);
    }

    /// <summary>Período em meses (primeiro dia de cada): padrão os últimos 12 meses até o corrente; até 24.</summary>
    private (DateOnly From, DateOnly To)? Period(MaintenanceCostQuery query)
    {
        var today = BusinessTime.Today(clock);
        var current = new DateOnly(today.Year, today.Month, 1);
        var to = query.To is null ? current : ParseMonth(query.To);
        var from = query.From is null ? to?.AddMonths(-11) : ParseMonth(query.From);
        if (from is not { } f || to is not { } t || t < f || (t.Year - f.Year) * 12 + t.Month - f.Month + 1 > MaxMonths)
        {
            return null;
        }

        return (f, t);
    }

    private static DateOnly? ParseMonth(string text) =>
        DateOnly.TryParseExact(text + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d.Year >= 2000 ? d : null;

    private static string MonthKey(DateOnly date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private async Task<MaintenanceCostsView> ViewAsync(MaintenanceOrder order, CancellationToken ct)
    {
        var lines = await db.MaintenanceOrderCosts.AsNoTracking().Where(c => c.OrderId == order.Id).OrderBy(c => c.CreatedAt).ToListAsync(ct);
        var sensitive = await permissions.HasPermissionAsync("hr", "view_sensitive", ct);
        var employeeIds = sensitive ? lines.Select(l => l.EmployeeId).OfType<Guid>().Distinct().ToList() : [];
        var names = employeeIds.Count == 0 ? [] : (await employees.FindAsync(employeeIds, ct)).ToDictionary(e => e.Id, e => e.Name);
        var views = lines.Select(l =>
        {
            var labor = l.CostType == MaintenanceCostType.Labor;
            return new MaintenanceCostLineView(l.Id, l.CostType, l.Description, labor && !sensitive ? null : l.Amount, l.Hours, sensitive ? l.HourlyCostSnapshot : null,
                sensitive && l.EmployeeId is { } e ? names.GetValueOrDefault(e) : null, l.PayableId, l.PayableId is null && order.Status == MaintenanceOrderStatus.Done, l.CreatedAt);
        }).ToList();
        var totals = Enum.GetValues<MaintenanceCostType>().Select(t => new MaintenanceCostTotal(t, lines.Where(l => l.CostType == t).Sum(l => l.Amount))).ToList();
        return new MaintenanceCostsView(order.Id, order.Status, views, totals, lines.Sum(l => l.Amount), sensitive, order.Version);
    }

    private async Task<Dictionary<Guid, string>> CenterNamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.OfType<Guid>().Distinct().ToList();
        return list.Count == 0 ? [] : (await workCenters.FindAsync(list, ct)).ToDictionary(c => c.Id, c => c.Name);
    }

    private static string? Clean(string? text)
    {
        var value = AssetRules.Clean(text);
        return value is null || value.Length > MaintenanceRules.CostDescriptionMax ? null : value;
    }

    private static Error NotFound() => Error.NotFound(MaintenanceService.OrderNotFoundCode, "OS de manutenção não encontrada.");
}
