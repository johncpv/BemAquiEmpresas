using BemAqui.Assets.Contracts;
using BemAqui.Assets.Domain;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Assets.Application;

/// <summary>
/// Manutenção do bem (PAT-03a, ADR 0038 §1): planos preventivos, horímetro digitado, OS de manutenção (<c>MAN</c>), aba Manutenção do bem e
/// Patrimônio → Manutenção. A situação do bem acompanha as OS ("Em manutenção" enquanto alguma o segura) na mesma transação da OS. O prestador
/// é validado pelo <c>IParties</c> (papel Fornecedor); o centro vem do <c>IWorkCenters</c>.
/// </summary>
public sealed partial class MaintenanceService(
    IAssetsDb db,
    INumberSequence numbers,
    IParties parties,
    IWorkCenters workCenters,
    IUserDirectory users,
    ICurrentUser currentUser,
    IEventPublisher events,
    AttachmentService attachments,
    ITenantContext tenant,
    TimeProvider clock)
{
    public const string DocType = "MAN";
    public const string DueIndex = "ux_maintenance_orders_plan_due_key";
    public const string AttachmentOwner = "MaintenanceOrder";
    public const string OrderLink = "/app/patrimonio/manutencao/";
    public const string PlanNotFoundCode = "maintenance_plan_not_found";
    public const string OrderNotFoundCode = "maintenance_order_not_found";
    public const string StatusCode = "maintenance_order_status";
    public const string ChecklistIncompleteCode = "maintenance_checklist_incomplete";
    public const string MeterNotGreaterCode = "meter_reading_not_greater";
    public const string PlanHasOpenOrderCode = "maintenance_plan_has_open_order";
    public const string OpenOrdersCode = "asset_has_open_maintenance";
    public const string OpenOrdersMessage = "Conclua ou cancele as OS de manutenção abertas.";

    private DateOnly Today => BusinessTime.Today(clock);

    /// <summary>Aba Manutenção do bem (CA 9): planos (os ativos primeiro, pelo vencimento) e as OS da mais recente à mais antiga.</summary>
    public async Task<Result<AssetMaintenanceView>> ForAssetAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        if (!await db.Assets.AsNoTracking().AnyAsync(a => a.Id == assetId, cancellationToken))
        {
            return AssetService.NotFound();
        }

        var plans = await db.MaintenancePlans.AsNoTracking().Where(p => p.AssetId == assetId)
            .OrderByDescending(p => p.IsActive).ThenBy(p => p.NextDueAt).ThenBy(p => p.Description).ToListAsync(cancellationToken);
        var orders = await db.MaintenanceOrders.AsNoTracking().Where(o => o.AssetId == assetId)
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Number).Take(200).ToListAsync(cancellationToken);
        var last = await db.AssetMeterReadings.AsNoTracking().Where(r => r.AssetId == assetId)
            .OrderByDescending(r => r.ReadOn).ThenByDescending(r => r.Value).FirstOrDefaultAsync(cancellationToken);
        return new AssetMaintenanceView(await PlanViewsAsync(plans, cancellationToken), await OrderItemsAsync(orders, cancellationToken),
            last is null ? null : (await ReadingViewsAsync([last], cancellationToken))[0]);
    }

    /// <summary>Um plano pela rota do bem (CA 13): o plano de outro bem é 404 (IDOR).</summary>
    public async Task<Result<MaintenancePlanView>> GetPlanAsync(Guid assetId, Guid planId, CancellationToken cancellationToken = default)
    {
        var plan = await db.MaintenancePlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId && p.AssetId == assetId, cancellationToken);
        return plan is null ? PlanNotFound() : (await PlanViewsAsync([plan], cancellationToken))[0];
    }

    /// <summary>"Salvar plano" (CA 1): o plano nasce ativo e calcula o próximo vencimento a partir da última execução.</summary>
    public async Task<Result<MaintenancePlanView>> CreatePlanAsync(Guid assetId, MaintenancePlanInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var asset = await db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == assetId, cancellationToken);
        if (asset is null)
        {
            return AssetService.NotFound();
        }

        if (asset.Status == AssetStatus.Disposed)
        {
            return Error.Conflict(AssetService.DisposedCode, "Bem baixado não recebe plano de manutenção. Reative o bem primeiro.");
        }

        var plan = new MaintenancePlan { AssetId = assetId, CreatedAt = clock.GetUtcNow() };
        if (await ApplyPlanAsync(plan, input, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        db.MaintenancePlans.Add(plan);
        await db.SaveChangesAsync(cancellationToken);
        return (await PlanViewsAsync([plan], cancellationToken))[0];
    }

    /// <summary>Editar o plano (CA 1 e 12): recalcula o próximo vencimento; o plano do bem baixado só é reativado com o bem reativado.</summary>
    public async Task<Result<MaintenancePlanView>> UpdatePlanAsync(Guid assetId, Guid planId, MaintenancePlanInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var plan = await db.MaintenancePlans.FirstOrDefaultAsync(p => p.Id == planId && p.AssetId == assetId, cancellationToken);
        if (plan is null)
        {
            return PlanNotFound();
        }

        if (input.Version is { } version)
        {
            db.Entry(plan).Property(p => p.Version).OriginalValue = version;
        }

        var status = await db.Assets.AsNoTracking().Where(a => a.Id == assetId).Select(a => a.Status).FirstAsync(cancellationToken);
        if (status == AssetStatus.Disposed && (input.IsActive ?? plan.IsActive))
        {
            return Error.Conflict(AssetService.DisposedCode, "O bem está baixado: o plano fica inativo. Reative o bem para reativar o plano.");
        }

        if (await ApplyPlanAsync(plan, input, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        if (input.IsActive is { } active)
        {
            plan.IsActive = active;
        }

        await db.SaveChangesAsync(cancellationToken);
        return (await PlanViewsAsync([plan], cancellationToken))[0];
    }

    /// <summary>Leituras do horímetro do bem, da mais recente à mais antiga.</summary>
    public async Task<Result<IReadOnlyList<MeterReadingView>>> ReadingsAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        if (!await db.Assets.AsNoTracking().AnyAsync(a => a.Id == assetId, cancellationToken))
        {
            return AssetService.NotFound();
        }

        var rows = await db.AssetMeterReadings.AsNoTracking().Where(r => r.AssetId == assetId)
            .OrderByDescending(r => r.ReadOn).ThenByDescending(r => r.Value).Take(100).ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<MeterReadingView>>(await ReadingViewsAsync(rows, cancellationToken));
    }

    /// <summary>
    /// "Registrar horímetro" (CA 2): valor e data (até hoje, não antes da última leitura), sempre maior que a anterior (422). Os planos por horas
    /// do bem recalculam a data estimada pela média das últimas leituras.
    /// </summary>
    public async Task<Result<MeterReadingResult>> AddReadingAsync(Guid assetId, MeterReadingInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var asset = await db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == assetId, cancellationToken);
        if (asset is null)
        {
            return AssetService.NotFound();
        }

        if (asset.Status == AssetStatus.Disposed)
        {
            return Error.Conflict(AssetService.DisposedCode, "Bem baixado não recebe leitura do horímetro. Reative o bem primeiro.");
        }

        var (reading, error) = await NewReadingAsync(assetId, input.Value, input.ReadOn, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        db.AssetMeterReadings.Add(reading!);
        var plans = await db.MaintenancePlans.Where(p => p.AssetId == assetId && p.FrequencyType == MaintenanceFrequency.Hours).ToListAsync(cancellationToken);
        var points = await PointsAsync(assetId, reading, cancellationToken);
        foreach (var plan in plans)
        {
            plan.Recalculate(points);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new MeterReadingResult((await ReadingViewsAsync([reading!], cancellationToken))[0], await PlanViewsAsync(plans, cancellationToken));
    }

    /// <summary>
    /// Patrimônio → Manutenção (CA 10): as OS do filtro (sem situação, as abertas e em andamento; a de máquina parada primeiro) e, sem filtro de
    /// situação, as preventivas vencidas e as que vencem nos próximos 30 dias.
    /// </summary>
    public async Task<MaintenanceOverview> OverviewAsync(MaintenanceListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var orders = from o in db.MaintenanceOrders.AsNoTracking()
                     join a in db.Assets.AsNoTracking() on o.AssetId equals a.Id
                     select new { o, a.WorkCenterId };
        if (query.AssetId is { } asset)
        {
            orders = orders.Where(x => x.o.AssetId == asset);
        }

        if (query.WorkCenterId is { } center)
        {
            orders = orders.Where(x => x.WorkCenterId == center);
        }

        if (query.Type is { } type)
        {
            orders = orders.Where(x => x.o.Type == type);
        }

        orders = query.Status is { } status
            ? orders.Where(x => x.o.Status == status)
            : orders.Where(x => x.o.Status == MaintenanceOrderStatus.Open || x.o.Status == MaintenanceOrderStatus.InProgress);
        var ordered = query.Status is MaintenanceOrderStatus.Done or MaintenanceOrderStatus.Cancelled
            ? orders.OrderByDescending(x => x.o.DoneAt ?? x.o.CancelledAt).ThenByDescending(x => x.o.Number)
            : orders.OrderByDescending(x => x.o.MachineStopped).ThenBy(x => x.o.ScheduledOn).ThenBy(x => x.o.Number);
        var rows = await ordered.Select(x => x.o).Take(200).ToListAsync(cancellationToken);
        var items = await OrderItemsAsync(rows, cancellationToken);
        var due = query.Status is null && query.Type is null or MaintenanceOrderType.Preventive
            ? await DuePlansAsync(query.AssetId, query.WorkCenterId, MaintenanceRules.DueSoonDays, cancellationToken)
            : [];
        return new MaintenanceOverview(items, due);
    }

    /// <summary>Preventivas ativas vencidas ou que vencem em até <paramref name="days"/> dias, de bens não baixados (CA 10 e 11).</summary>
    internal async Task<IReadOnlyList<MaintenanceDuePlan>> DuePlansAsync(Guid? assetId, Guid? workCenterId, int days, CancellationToken ct)
    {
        var plans = from p in db.MaintenancePlans.AsNoTracking()
                    join a in db.Assets.AsNoTracking() on p.AssetId equals a.Id
                    where p.IsActive && a.Status != AssetStatus.Disposed
                    select new { p, a.Tag, a.Name, a.WorkCenterId };
        if (assetId is { } asset)
        {
            plans = plans.Where(x => x.p.AssetId == asset);
        }

        if (workCenterId is { } center)
        {
            plans = plans.Where(x => x.WorkCenterId == center);
        }

        var limit = Today.AddDays(days);
        var rows = await plans.Where(x => x.p.FrequencyType == MaintenanceFrequency.Hours || (x.p.NextDueAt != null && x.p.NextDueAt <= limit)).Take(1000).ToListAsync(ct);
        var meters = await CurrentMetersAsync(rows.Select(r => r.p.AssetId).Distinct().ToList(), ct);
        var open = await OpenOrderByPlanAsync(rows.Select(r => r.p.Id).ToList(), ct);
        var centers = await CenterNamesAsync(rows.Select(r => r.WorkCenterId), ct);
        return rows
            .Select(r => (Row: r, Meter: meters.TryGetValue(r.p.AssetId, out var m) ? m : (decimal?)null))
            .Select(x => (x.Row, x.Meter, Overdue: MaintenanceRules.IsOverdue(x.Row.p.FrequencyType, x.Row.p.NextDueAt, x.Row.p.NextDueMeter, x.Meter, Today)))
            .Where(x => x.Overdue || MaintenanceRules.IsDue(x.Row.p.FrequencyType, x.Row.p.NextDueAt, x.Row.p.NextDueMeter, x.Meter, Today) || (x.Row.p.NextDueAt is { } d && d <= limit))
            .OrderByDescending(x => x.Overdue).ThenBy(x => x.Row.p.NextDueAt ?? DateOnly.MaxValue).ThenBy(x => x.Row.Tag)
            .Select(x => new MaintenanceDuePlan(
                x.Row.p.Id, x.Row.p.AssetId, x.Row.Tag, x.Row.Name, x.Row.WorkCenterId, x.Row.WorkCenterId is { } c ? centers.GetValueOrDefault(c) : null, x.Row.p.Description,
                x.Row.p.FrequencyType, x.Row.p.NextDueAt, x.Row.p.NextDueMeter, x.Meter, x.Overdue,
                open.TryGetValue(x.Row.p.Id, out var o) ? o.Id : null, open.TryGetValue(x.Row.p.Id, out var n) ? n.Number : null))
            .ToList();
    }

    private async Task<Error?> ApplyPlanAsync(MaintenancePlan plan, MaintenancePlanInput input, CancellationToken ct)
    {
        var description = AssetRules.Clean(input.Description);
        if (description is null || description.Length > MaintenanceRules.DescriptionMax)
        {
            return AssetService.Invalid("description", "Informe a descrição do plano, com até 150 caracteres.");
        }

        if (input.FrequencyType is not { } type || !Enum.IsDefined(type))
        {
            return AssetService.Invalid("frequencyType", "Escolha a periodicidade: a cada N dias ou a cada N horas de uso.");
        }

        var max = type == MaintenanceFrequency.Days ? MaintenanceRules.DaysMax : MaintenanceRules.HoursMax;
        if (input.FrequencyValue is not { } value || value < 1 || value > max)
        {
            return AssetService.Invalid("frequencyValue", type == MaintenanceFrequency.Days
                ? $"Informe a cada quantos dias, de 1 a {MaintenanceRules.DaysMax}."
                : "Informe a cada quantas horas de uso, de 1 a 100.000.");
        }

        var lead = input.LeadDays ?? MaintenanceRules.DefaultLeadDays;
        if (lead is < 0 or > MaintenanceRules.LeadDaysMax)
        {
            return AssetService.Invalid("leadDays", $"Informe a antecedência do aviso de 0 a {MaintenanceRules.LeadDaysMax} dias.");
        }

        var checklist = MaintenanceRules.CleanChecklist(input.Checklist);
        if (ChecklistError(checklist) is { } badChecklist)
        {
            return badChecklist;
        }

        if (input.LastDoneAt is { } doneAt && (doneAt > Today || doneAt.Year < 1900))
        {
            return AssetService.Invalid("lastDoneAt", "A data da última execução não pode ser futura.");
        }

        if (type == MaintenanceFrequency.Days && input.LastDoneAt is null)
        {
            return AssetService.Invalid("lastDoneAt", "Informe a data da última execução.");
        }

        if (type == MaintenanceFrequency.Hours && (input.LastDoneMeter is not { } meter || !MaintenanceRules.IsValidMeter(meter)))
        {
            return AssetService.Invalid("lastDoneMeter", "Informe o horímetro da última execução, de zero para cima.");
        }

        if (input.DefaultSupplierPartyId is { } supplier && supplier != plan.DefaultSupplierPartyId && !await IsActiveSupplierAsync(supplier, ct))
        {
            return AssetService.Invalid("defaultSupplierPartyId", "Escolha um fornecedor ativo da lista.");
        }

        plan.Description = description;
        plan.FrequencyType = type;
        plan.FrequencyValue = value;
        plan.LeadDays = lead;
        plan.DefaultSupplierPartyId = input.DefaultSupplierPartyId;
        plan.Checklist = checklist;
        plan.LastDoneAt = input.LastDoneAt;
        plan.LastDoneMeter = type == MaintenanceFrequency.Hours ? input.LastDoneMeter : null;
        plan.Recalculate(await PointsAsync(plan.AssetId, null, ct));
        return null;
    }

    private static Error? ChecklistError(List<string> checklist)
    {
        if (checklist.Count > MaintenanceRules.ChecklistMax)
        {
            return AssetService.Invalid("checklist", $"O checklist aceita até {MaintenanceRules.ChecklistMax} itens.");
        }

        return checklist.Any(i => i.Length > MaintenanceRules.ChecklistItemMax) ? AssetService.Invalid("checklist", "Cada item do checklist aceita até 200 caracteres.") : null;
    }

    private async Task<bool> IsActiveSupplierAsync(Guid partyId, CancellationToken ct) =>
        await parties.FindAsync(partyId, ct) is { IsActive: true, IsDeleted: false } party && party.Roles.Contains(PartyRoleKind.Supplier);

    /// <summary>Leitura nova (CA 2): valor válido, data até hoje e não antes da última, sempre maior que a anterior.</summary>
    private async Task<(AssetMeterReading? Reading, Error? Error)> NewReadingAsync(Guid assetId, decimal? value, DateOnly? readOn, CancellationToken ct)
    {
        if (value is not { } v || !MaintenanceRules.IsValidMeter(v))
        {
            return (null, AssetService.Invalid("value", "Informe a leitura do horímetro, de zero para cima."));
        }

        var date = readOn ?? Today;
        if (date > Today || date.Year < 1900)
        {
            return (null, AssetService.Invalid("readOn", "A data da leitura não pode ser futura."));
        }

        var last = await db.AssetMeterReadings.AsNoTracking().Where(r => r.AssetId == assetId)
            .OrderByDescending(r => r.Value).Select(r => new { r.Value, r.ReadOn }).FirstOrDefaultAsync(ct);
        if (last is not null && v <= last.Value)
        {
            var message = $"A leitura precisa ser maior que a anterior ({MaintenanceRules.MeterText(last.Value)}).";
            return (null, Error.Validation(MeterNotGreaterCode, message, new Dictionary<string, string[]> { ["value"] = [message] }));
        }

        if (last is not null && date < last.ReadOn)
        {
            return (null, AssetService.Invalid("readOn", $"A data não pode ser anterior à última leitura ({last.ReadOn:dd/MM/yyyy})."));
        }

        return (new AssetMeterReading { AssetId = assetId, ReadOn = date, Value = v, CreatedBy = currentUser.UserId, CreatedAt = clock.GetUtcNow() }, null);
    }

    /// <summary>Leituras do bem para as contas (a nova ainda não gravada entra junto).</summary>
    private async Task<List<MeterPoint>> PointsAsync(Guid assetId, AssetMeterReading? pending, CancellationToken ct)
    {
        var points = await db.AssetMeterReadings.AsNoTracking().Where(r => r.AssetId == assetId)
            .OrderByDescending(r => r.ReadOn).ThenByDescending(r => r.Value).Take(MaintenanceRules.ReadingsForAverage)
            .Select(r => new MeterPoint(r.ReadOn, r.Value)).ToListAsync(ct);
        if (pending is not null)
        {
            points.Add(new MeterPoint(pending.ReadOn, pending.Value));
        }

        return points;
    }

    /// <summary>Horímetro atual (a maior leitura) de cada bem.</summary>
    internal async Task<Dictionary<Guid, decimal>> CurrentMetersAsync(IReadOnlyCollection<Guid> assetIds, CancellationToken ct) =>
        assetIds.Count == 0 ? []
            : await db.AssetMeterReadings.AsNoTracking().Where(r => assetIds.Contains(r.AssetId)).GroupBy(r => r.AssetId)
                .Select(g => new { g.Key, Max = g.Max(r => r.Value) }).ToDictionaryAsync(x => x.Key, x => x.Max, ct);

    /// <summary>A OS aberta ou em andamento de cada plano (no máximo uma é esperada; a mais recente vale).</summary>
    private async Task<Dictionary<Guid, (Guid Id, string Number)>> OpenOrderByPlanAsync(List<Guid> planIds, CancellationToken ct)
    {
        if (planIds.Count == 0)
        {
            return [];
        }

        var rows = await db.MaintenanceOrders.AsNoTracking()
            .Where(o => o.PlanId != null && planIds.Contains(o.PlanId.Value) && (o.Status == MaintenanceOrderStatus.Open || o.Status == MaintenanceOrderStatus.InProgress))
            .OrderBy(o => o.CreatedAt).Select(o => new { PlanId = o.PlanId!.Value, o.Id, o.Number }).ToListAsync(ct);
        var map = new Dictionary<Guid, (Guid, string)>();
        foreach (var row in rows)
        {
            map[row.PlanId] = (row.Id, row.Number);
        }

        return map;
    }

    private async Task<Dictionary<Guid, string>> CenterNamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.OfType<Guid>().Distinct().ToList();
        return list.Count == 0 ? [] : (await workCenters.FindAsync(list, ct)).ToDictionary(c => c.Id, c => c.Name);
    }

    private async Task<Dictionary<Guid, string>> PartyNamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.OfType<Guid>().Distinct().ToList();
        return list.Count == 0 ? [] : (await parties.FindManyAsync(list, ct)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
    }

    private async Task<Dictionary<Guid, string?>> UserNamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string?>();
        foreach (var user in ids.OfType<Guid>().Distinct())
        {
            names[user] = await users.DisplayNameAsync(user, ct);
        }

        return names;
    }

    private async Task<List<MaintenancePlanView>> PlanViewsAsync(IReadOnlyList<MaintenancePlan> plans, CancellationToken ct)
    {
        var suppliers = await PartyNamesAsync(plans.Select(p => p.DefaultSupplierPartyId), ct);
        var meters = await CurrentMetersAsync(plans.Select(p => p.AssetId).Distinct().ToList(), ct);
        var open = await OpenOrderByPlanAsync(plans.Select(p => p.Id).ToList(), ct);
        var today = Today;
        return plans.Select(p =>
        {
            decimal? meter = meters.TryGetValue(p.AssetId, out var m) ? m : null;
            var overdue = p.IsActive && MaintenanceRules.IsOverdue(p.FrequencyType, p.NextDueAt, p.NextDueMeter, meter, today);
            var soon = p.IsActive && !overdue && (MaintenanceRules.IsDue(p.FrequencyType, p.NextDueAt, p.NextDueMeter, meter, today) || MaintenanceRules.InLead(p.NextDueAt, p.LeadDays, today));
            return new MaintenancePlanView(
                p.Id, p.AssetId, p.Description, p.FrequencyType, p.FrequencyValue, p.LeadDays, p.DefaultSupplierPartyId,
                p.DefaultSupplierPartyId is { } s ? suppliers.GetValueOrDefault(s) : null, p.Checklist, p.LastDoneAt, p.LastDoneMeter, p.NextDueAt, p.NextDueMeter, meter,
                p.IsActive, overdue, soon, open.TryGetValue(p.Id, out var o) ? o.Id : null, open.TryGetValue(p.Id, out var n) ? n.Number : null, p.Version);
        }).ToList();
    }

    private async Task<List<MeterReadingView>> ReadingViewsAsync(IReadOnlyList<AssetMeterReading> rows, CancellationToken ct)
    {
        var names = await UserNamesAsync(rows.Select(r => r.CreatedBy), ct);
        return rows.Select(r => new MeterReadingView(r.Id, r.ReadOn, r.Value, r.CreatedBy is { } u ? names.GetValueOrDefault(u) : null, r.CreatedAt)).ToList();
    }

    private async Task<List<MaintenanceOrderListItem>> OrderItemsAsync(IReadOnlyList<MaintenanceOrder> orders, CancellationToken ct)
    {
        var assetIds = orders.Select(o => o.AssetId).Distinct().ToList();
        var assets = assetIds.Count == 0 ? []
            : await db.Assets.IgnoreQueryFilters().AsNoTracking().Where(a => assetIds.Contains(a.Id)).Select(a => new { a.Id, a.Tag, a.Name, a.WorkCenterId }).ToDictionaryAsync(a => a.Id, ct);
        var planIds = orders.Select(o => o.PlanId).OfType<Guid>().Distinct().ToList();
        var plans = planIds.Count == 0 ? []
            : await db.MaintenancePlans.AsNoTracking().Where(p => planIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Description, ct);
        var centers = await CenterNamesAsync(assets.Values.Select(a => a.WorkCenterId), ct);
        var suppliers = await PartyNamesAsync(orders.Select(o => o.SupplierPartyId), ct);
        return orders.Select(o =>
        {
            var a = assets[o.AssetId];
            return new MaintenanceOrderListItem(
                o.Id, o.Number, o.AssetId, a.Tag, a.Name, a.WorkCenterId, a.WorkCenterId is { } c ? centers.GetValueOrDefault(c) : null, o.Type, o.Status, o.MachineStopped,
                o.ScheduledOn, o.StartedAt, o.DoneAt, o.CancelledAt, o.SupplierPartyId, o.SupplierPartyId is { } s ? suppliers.GetValueOrDefault(s) : null,
                o.PlanId, o.PlanId is { } p ? plans.GetValueOrDefault(p) : null, o.CreatedAt);
        }).ToList();
    }

    private static Error PlanNotFound() => Error.NotFound(PlanNotFoundCode, "Plano de manutenção não encontrado.");
}
