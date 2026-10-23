using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BemAqui.Purchasing.Application;

/// <summary>
/// Recomposição das solicitações pela falta da BOM (CA 5 a 8, ADR 0023 §6). O mesmo código serve ao consumidor de
/// <see cref="StockShortageChanged"/>, à recomposição da implantação e ao job diário: lê as faltas abertas da OS na fonte
/// (<see cref="IStockReservations.OpenShortagesAsync"/>), nunca o valor do evento, e leva a linha <c>BomShortage</c> em aberto de cada
/// item até a falta atual menos o que já está em cotação ou pedido. Uma trava por OS (<c>pg_advisory_xact_lock</c>) põe em fila duas
/// recomposições da mesma OS; o índice único parcial das linhas abertas é a rede de segurança. Com o módulo <c>purchasing</c> não
/// liberado, não escreve nada (CA 8).
/// </summary>
public sealed partial class ShortageRecomposer(
    IPurchasingDb db, IStockReservations reservations, IProductionOrders orders, IProjects projects, IItems items, PurchasingParametersService parameters,
    INumberSequence numbers, IUserDirectory users, INotificationService notifications, AuditOrigin origin, TenantContextHolder tenant, TimeProvider clock,
    ILogger<ShortageRecomposer> logger)
{
    public const string Module = "purchasing";
    public const string DocType = "SC";
    public const string DropNoticeType = "purchasing.shortage_dropped";
    public const string Link = "/app/compras/solicitacoes/";
    public const string OnboardingLabel = "Sistema · Calculado na implantação";

    public bool ModuleReleased => tenant.Resolved?.IsReleased(Module) == true;

    /// <summary>"Sistema · Falta da BOM {OS}" (CA 17).</summary>
    public static string ShortageLabel(string? orderNumber) => "Sistema · Falta da BOM " + (orderNumber ?? "OS");

    /// <summary>"A falta de {item} na {OS} caiu para {x}; a linha já está em {cotação/pedido}." (CA 6).</summary>
    public static string DropNotice(string item, string? orderNumber, decimal shortage, PurchaseRequestStatus status) =>
        $"A falta de {item} na {orderNumber ?? "OS"} caiu para {PurchaseFormat.Quantity(shortage)}; a linha já está em {(status == PurchaseRequestStatus.Ordered ? "pedido" : "cotação")}.";

    /// <summary>
    /// Recompõe as linhas da falta de uma OS, na transação do chamador (a da inbox ou a da recomposição). Devolve quantas linhas mudaram.
    /// </summary>
    public async Task<int> RecomposeOrderAsync(Guid productionOrderId, bool onboarding = false, CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A recomposição exige transação aberta: o chamador é dono da transação.");
        }

        if (!ModuleReleased)
        {
            LogNotReleased(logger, productionOrderId);
            return 0;
        }

        await LockOrderAsync(productionOrderId, cancellationToken);
        var shortages = (await reservations.OpenShortagesAsync(cancellationToken)).Where(s => s.ProductionOrderId == productionOrderId)
            .GroupBy(s => s.ItemId).ToDictionary(g => g.Key, g => (Qty: g.Sum(s => s.ShortQty), BomLineId: g.Select(s => s.BomLineId).FirstOrDefault(b => b is not null), g.First().ProjectId));
        var lines = await db.PurchaseRequestLines.Where(l => l.ProductionOrderId == productionOrderId && l.Origin == PurchaseRequestOrigin.BomShortage).ToListAsync(cancellationToken);
        if (shortages.Count == 0 && lines.All(l => l.Status != PurchaseRequestStatus.Open))
        {
            return 0;
        }

        var order = await orders.FindAsync(productionOrderId, cancellationToken);
        var projectId = shortages.Values.Select(s => s.ProjectId).FirstOrDefault(p => p != Guid.Empty);
        if (projectId == Guid.Empty)
        {
            projectId = order?.ProjectId ?? lines.Select(l => l.ProjectId).FirstOrDefault(p => p is not null) ?? Guid.Empty;
        }

        var project = projectId == Guid.Empty ? null : await projects.FindAsync(projectId, cancellationToken);
        var today = BusinessTime.Today(clock);
        var neededBy = PurchaseRequestRules.ShortageNeededBy(project?.PromisedDate, today, await parameters.LeadDaysAsync(cancellationToken));
        var previousLabel = origin.Label;
        origin.Label = onboarding ? OnboardingLabel : ShortageLabel(order?.Number);
        var now = clock.GetUtcNow();
        var changed = 0;
        var touched = new HashSet<Guid>();
        PurchaseRequest? openRequest = null;
        var notices = new List<(Guid ItemId, decimal Shortage, PurchaseRequestStatus Status, Guid RequestId)>();
        foreach (var itemId in shortages.Keys.Union(lines.Select(l => l.ItemId)).Distinct().Order())
        {
            var mine = lines.Where(l => l.ItemId == itemId).ToList();
            var source = shortages.GetValueOrDefault(itemId);
            var plan = PurchaseRequestRules.PlanShortage(source.Qty, mine.Select(l => new ShortageLineState(l.Id, l.Status, l.Quantity, l.ManuallyEdited)).ToList());
            if (plan.Skip)
            {
                continue;
            }

            if (plan.DropNotice is { } drop)
            {
                var committed = mine.First(l => l.Status is PurchaseRequestStatus.Quoting or PurchaseRequestStatus.Ordered);
                notices.Add((itemId, drop, plan.DropStatus!.Value, committed.RequestId));
            }

            var open = mine.FirstOrDefault(l => l.Status == PurchaseRequestStatus.Open);
            if (plan.OpenTarget > 0m)
            {
                if (open is null)
                {
                    openRequest ??= await OpenRequestAsync(productionOrderId, projectId == Guid.Empty ? null : projectId, now, cancellationToken);
                    var sequence = await NextSequenceAsync(openRequest.Id, cancellationToken);
                    db.PurchaseRequestLines.Add(new PurchaseRequestLine
                    {
                        RequestId = openRequest.Id, Origin = PurchaseRequestOrigin.BomShortage, Sequence = sequence, ItemId = itemId, Quantity = plan.OpenTarget, NeededBy = neededBy,
                        ProductionOrderId = productionOrderId, BomLineId = source.BomLineId, ProjectId = openRequest.ProjectId, CalculatedOnOnboarding = onboarding, CreatedAt = now,
                        UpdatedAt = now,
                    });
                    touched.Add(openRequest.Id);
                    changed++;
                }
                else if (open.Quantity != plan.OpenTarget || open.NeededBy != neededBy || (source.BomLineId is not null && open.BomLineId != source.BomLineId))
                {
                    open.Quantity = plan.OpenTarget;
                    open.NeededBy = neededBy;
                    open.BomLineId = source.BomLineId ?? open.BomLineId;
                    open.EnteredQuantity = null;
                    open.EnteredUnitId = null;
                    open.UpdatedAt = now;
                    touched.Add(open.RequestId);
                    changed++;
                }
            }
            else if (open is not null)
            {
                PurchaseRequestUpdates.Cancel(open, PurchaseRequestRules.ShortageResolvedReason, now);
                touched.Add(open.RequestId);
                changed++;
            }
        }

        if (touched.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            await PurchaseRequestUpdates.RefreshStatusAsync(db, touched, now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        await NotifyDropsAsync(notices, order?.Number, productionOrderId, cancellationToken);
        origin.Label = previousLabel;
        return changed;
    }

    /// <summary>
    /// Recompõe todas as OS com falta aberta e as que ainda têm linha de falta em aberto (para cancelar o que zerou), uma OS por
    /// transação (CA 7). Idempotente: a recomposição leva ao alvo.
    /// </summary>
    public async Task<int> RecomposeAllAsync(bool onboarding, CancellationToken cancellationToken = default)
    {
        if (!ModuleReleased)
        {
            LogNotReleased(logger, Guid.Empty);
            return 0;
        }

        var withShortage = (await reservations.OpenShortagesAsync(cancellationToken)).Select(s => s.ProductionOrderId);
        var withOpenLine = await db.PurchaseRequestLines.AsNoTracking()
            .Where(l => l.Origin == PurchaseRequestOrigin.BomShortage && l.Status == PurchaseRequestStatus.Open && l.ProductionOrderId != null)
            .Select(l => l.ProductionOrderId!.Value).Distinct().ToListAsync(cancellationToken);
        var changed = 0;
        foreach (var orderId in withShortage.Union(withOpenLine).Distinct().Order())
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            changed += await RecomposeOrderAsync(orderId, onboarding, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return changed;
    }

    /// <summary>A SC aberta da OS (uma por OS) ou uma nova, com o número na mesma transação.</summary>
    private async Task<PurchaseRequest> OpenRequestAsync(Guid orderId, Guid? projectId, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.PurchaseRequests.FirstOrDefaultAsync(
            r => r.Origin == PurchaseRequestOrigin.BomShortage && r.ProductionOrderId == orderId && r.Status == PurchaseRequestStatus.Open, ct);
        if (existing is not null)
        {
            return existing;
        }

        var request = new PurchaseRequest
        {
            Number = await numbers.NextAsync(DocType, ct), Origin = PurchaseRequestOrigin.BomShortage, ProjectId = projectId, ProductionOrderId = orderId, CreatedAt = now, UpdatedAt = now,
        };
        db.PurchaseRequests.Add(request);
        return request;
    }

    private async Task<int> NextSequenceAsync(Guid requestId, CancellationToken ct)
    {
        var saved = await db.PurchaseRequestLines.Where(l => l.RequestId == requestId).Select(l => (int?)l.Sequence).MaxAsync(ct) ?? 0;
        var pending = db.PurchaseRequestLines.Local.Where(l => l.RequestId == requestId).Select(l => l.Sequence).DefaultIfEmpty(0).Max();
        return Math.Max(saved, pending) + 1;
    }

    private async Task NotifyDropsAsync(List<(Guid ItemId, decimal Shortage, PurchaseRequestStatus Status, Guid RequestId)> notices, string? orderNumber, Guid orderId, CancellationToken ct)
    {
        if (notices.Count == 0)
        {
            return;
        }

        var names = (await items.FindAsync(notices.Select(n => n.ItemId).Distinct().ToList(), ct)).ToDictionary(i => i.Id, i => i.Code + " " + i.Name);
        var buyers = await users.UsersWithPermissionAsync("purchasing.edit", ct);
        foreach (var (itemId, shortage, status, requestId) in notices)
        {
            var text = DropNotice(names.GetValueOrDefault(itemId) ?? "item", orderNumber, shortage, status);
            var key = $"purchasing-shortage-drop:{orderId:N}:{itemId:N}:{PurchaseFormat.Key(shortage)}";
            foreach (var buyer in buyers)
            {
                await notifications.NotifyOnceAsync(buyer, DropNoticeType, text, Link + requestId, key, ct);
            }
        }
    }

    /// <summary>Uma recomposição por OS de cada vez (fonte lida e gravada sob a mesma trava).</summary>
    private async Task LockOrderAsync(Guid productionOrderId, CancellationToken ct)
    {
        var key = "purchasing.requests:" + productionOrderId.ToString("N");
        await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))) AS l").ToListAsync(ct);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Compras não liberado no tenant: recomposição da falta da OS {OrderId} não gravou nada (CMP-01 CA 8).")]
    private static partial void LogNotReleased(ILogger logger, Guid orderId);
}
