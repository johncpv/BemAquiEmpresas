using System.Globalization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Inventory.Contracts;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Purchasing.Application;

/// <summary>Resultado de uma execução do mínimo: linhas novas, atualizadas e canceladas.</summary>
public sealed record MinStockRunResult(int Created, int Updated, int Cancelled);

/// <summary>
/// Solicitação pelo estoque mínimo (CA 9 a 11, ponto 30). Para cada item ativo com mínimo &gt; 0: posição = disponível (todos os locais) +
/// linhas "Estoque geral" ainda não pedidas (fora a própria linha do mínimo em aberto) + saldo a receber dos pedidos em aberto; abaixo do
/// mínimo, cria ou atualiza <b>uma</b> linha <c>MinStock</c> em aberto até o mínimo; no mínimo, cancela a linha em aberto. As linhas novas do
/// dia entram numa SC da execução (a mesma no dia). Uma trava por tenant põe duas execuções em fila; o índice único parcial é a rede de
/// segurança. Linha editada à mão não muda. Com o módulo não liberado, nada (CA 8).
/// </summary>
public sealed partial class MinStockPurchaseRequests(
    IPurchasingDb db, IItems items, IStockBalances balances, IOpenPurchaseOrderQuantities purchaseOrders, PurchasingParametersService parameters, INumberSequence numbers,
    IUserDirectory users, INotificationService notifications, AuditOrigin origin, TenantContextHolder tenant, TimeProvider clock, ILogger<MinStockPurchaseRequests> logger)
{
    public const string NoticeType = "purchasing.min_stock";
    public const string OriginLabel = "Sistema · Estoque mínimo";
    public const string ListLink = "/app/compras/solicitacoes?origem=MinStock";

    /// <summary>"{n} itens abaixo do mínimo." (CA 11).</summary>
    public static string Notice(int count) => count == 1 ? "1 item abaixo do mínimo." : $"{count} itens abaixo do mínimo.";

    public async Task<MinStockRunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        if (tenant.Resolved?.IsReleased(ShortageRecomposer.Module) != true)
        {
            LogNotReleased(logger);
            return new MinStockRunResult(0, 0, 0);
        }

        var today = BusinessTime.Today(clock);
        var now = clock.GetUtcNow();
        var previousLabel = origin.Label;
        origin.Label = OriginLabel;
        MinStockRunResult result;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({"purchasing.min-stock"}, 0))) AS l").ToListAsync(cancellationToken);
            result = await ApplyAsync(today, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        origin.Label = previousLabel;
        if (result.Created > 0)
        {
            var iso = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            foreach (var buyer in await users.UsersWithPermissionAsync("purchasing.edit", cancellationToken))
            {
                await notifications.NotifyOnceAsync(buyer, NoticeType, Notice(result.Created), ListLink, $"min-stock:{iso}", cancellationToken);
            }
        }

        return result;
    }

    private async Task<MinStockRunResult> ApplyAsync(DateOnly today, DateTimeOffset now, CancellationToken ct)
    {
        var minimums = (await items.WithMinStockAsync(ct)).ToDictionary(i => i.ItemId, i => i.MinStock);
        var own = await db.PurchaseRequestLines.Where(l => l.Origin == PurchaseRequestOrigin.MinStock && l.Status == PurchaseRequestStatus.Open).ToListAsync(ct);
        var itemIds = minimums.Keys.Union(own.Select(l => l.ItemId)).Distinct().Order().ToList();
        if (itemIds.Count == 0)
        {
            return new MinStockRunResult(0, 0, 0);
        }

        var available = (await balances.GetAsync(itemIds, cancellationToken: ct)).GroupBy(b => b.ItemId).ToDictionary(g => g.Key, g => g.Sum(b => b.Available));
        var pending = await db.PurchaseRequestLines.AsNoTracking()
            .Where(l => itemIds.Contains(l.ItemId) && l.ProjectId == null && (l.Status == PurchaseRequestStatus.Open || l.Status == PurchaseRequestStatus.Quoting)
                && !(l.Origin == PurchaseRequestOrigin.MinStock && l.Status == PurchaseRequestStatus.Open))
            .GroupBy(l => l.ItemId).Select(g => new { g.Key, Quantity = g.Sum(l => l.Quantity - l.OrderedQuantity) }).ToDictionaryAsync(x => x.Key, x => x.Quantity, ct);
        var onOrder = await purchaseOrders.GeneralStockOpenQuantityAsync(itemIds, ct);
        var leadDays = await parameters.LeadDaysAsync(ct);
        PurchaseRequest? run = null;
        var touched = new HashSet<Guid>();
        int created = 0, updated = 0, cancelled = 0;
        foreach (var itemId in itemIds)
        {
            var line = own.FirstOrDefault(l => l.ItemId == itemId);
            if (line?.ManuallyEdited == true)
            {
                continue;
            }

            var position = PurchaseRequestRules.Position(available.GetValueOrDefault(itemId), pending.GetValueOrDefault(itemId), onOrder.GetValueOrDefault(itemId));
            var quantity = PurchaseRequestRules.MinStockQuantity(minimums.GetValueOrDefault(itemId), position);
            if (quantity > 0m && line is null)
            {
                run ??= await RunRequestAsync(today, now, ct);
                var sequence = db.PurchaseRequestLines.Local.Where(l => l.RequestId == run.Id).Select(l => l.Sequence).DefaultIfEmpty(0).Max();
                var saved = await db.PurchaseRequestLines.Where(l => l.RequestId == run.Id).Select(l => (int?)l.Sequence).MaxAsync(ct) ?? 0;
                db.PurchaseRequestLines.Add(new PurchaseRequestLine
                {
                    RequestId = run.Id, Origin = PurchaseRequestOrigin.MinStock, Sequence = Math.Max(sequence, saved) + 1, ItemId = itemId, Quantity = quantity,
                    NeededBy = PurchaseRequestRules.SuggestedNeededBy(today, leadDays), CreatedAt = now, UpdatedAt = now,
                });
                touched.Add(run.Id);
                created++;
            }
            else if (quantity > 0m && line!.Quantity != quantity)
            {
                line.Quantity = quantity;
                line.UpdatedAt = now;
                touched.Add(line.RequestId);
                updated++;
            }
            else if (quantity == 0m && line is not null)
            {
                PurchaseRequestUpdates.Cancel(line, PurchaseRequestRules.MinStockResolvedReason, now);
                touched.Add(line.RequestId);
                cancelled++;
            }
        }

        if (touched.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            await PurchaseRequestUpdates.RefreshStatusAsync(db, touched, now, ct);
            await db.SaveChangesAsync(ct);
        }

        return new MinStockRunResult(created, updated, cancelled);
    }

    /// <summary>A SC do mínimo da execução do dia (a mesma se o job rodar de novo no dia) ou uma nova.</summary>
    private async Task<PurchaseRequest> RunRequestAsync(DateOnly today, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.PurchaseRequests.FirstOrDefaultAsync(r => r.Origin == PurchaseRequestOrigin.MinStock && r.RunDate == today && r.Status == PurchaseRequestStatus.Open, ct);
        if (existing is not null)
        {
            return existing;
        }

        var request = new PurchaseRequest { Number = await numbers.NextAsync(ShortageRecomposer.DocType, ct), Origin = PurchaseRequestOrigin.MinStock, RunDate = today, CreatedAt = now, UpdatedAt = now };
        db.PurchaseRequests.Add(request);
        return request;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Compras não liberado no tenant: o job do estoque mínimo não gravou nada (CMP-01 CA 8).")]
    private static partial void LogNotReleased(ILogger logger);
}

/// <summary>Argumentos do job: só o tenant chega (pelo <see cref="TenantJob{TArgs}"/>), nada mais (CA 10).</summary>
public sealed record MinStockArgs;

/// <summary>
/// Execução diária por tenant (CA 9 e 7): primeiro a recomposição das faltas (a rede de segurança da implantação), depois o mínimo.
/// </summary>
public sealed class MinStockPurchaseRequestJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<MinStockPurchaseRequestJob> logger)
    : TenantJob<MinStockArgs>(directory, scopes, logger)
{
    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, MinStockArgs args, CancellationToken cancellationToken)
    {
        await services.GetRequiredService<ShortageBackfill>().RunAsync(cancellationToken);
        await services.GetRequiredService<MinStockPurchaseRequests>().RunAsync(cancellationToken);
    }
}

/// <summary>Recorrente global diário (06:00 de São Paulo; <c>Jobs:Cron:purchasing-min-stock</c> muda) que enfileira o job por tenant ativo.</summary>
public sealed class MinStockPurchaseRequestRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<MinStockPurchaseRequestJob, MinStockArgs>(directory, jobs)
{
    public const string Id = "purchasing-min-stock";
    public const string Cron = "0 6 * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override MinStockArgs ArgsFor(TenantInfo tenant) => new();
}
