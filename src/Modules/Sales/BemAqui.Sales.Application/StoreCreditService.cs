using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Pos.Contracts;
using BemAqui.Sales.Contracts;
using BemAqui.Sales.Domain;
using BemAqui.SharedKernel;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Sales.Application;

/// <summary>
/// Vale-troca como forma de pagamento (PDV-06 CA 8), só com rede: consultar pelo código e abater o valor de um pagamento do caixa (idempotente pelo id do
/// pagamento). O saldo restante continua no mesmo vale. A venda que não usou o abatimento (pagamento desfeito, venda descartada ou cancelada) devolve o
/// valor ao vale. A troca (CA 9) liga a venda nova à devolução na primeira venda que usa o vale.
/// </summary>
public sealed class StoreCreditService(ISalesDb db, IPosReturnSupport pos, TimeProvider clock)
    : IStoreCreditRedemptions, IIntegrationEventConsumer<SaleCompleted>, IIntegrationEventConsumer<SaleCancelled>, IIntegrationEventConsumer<PosSaleDiscarded>
{
    public const string RateLimitPolicy = "pos-store-credits";
    public const string NotFoundMessage = "Vale não encontrado. Confira o código.";

    public async Task<Result<StoreCreditView>> LookupAsync(string? posCookie, StoreCreditLookupInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await pos.ContextAsync(posCookie, cancellationToken) is null)
        {
            return Error.Forbidden(Error.Codes.Forbidden, SaleReturnService.NotRegisterMessage);
        }

        if (await FindAsync(input.Code, tracked: false, cancellationToken) is not { } credit)
        {
            return Error.NotFound(Error.Codes.NotFound, NotFoundMessage);
        }

        var problem = ReturnRules.RedeemProblem(credit.Balance, credit.ExpiresOn, BusinessTime.Today(clock), Math.Max(credit.Balance, 0.01m));
        return new StoreCreditView(credit.Id, credit.CodeLast4, credit.Amount, credit.Balance, credit.ExpiresOn, problem is null, problem);
    }

    public async Task<Result<StoreCreditRedeemed>> RedeemAsync(string? posCookie, Guid operatorId, StoreCreditRedeemInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await pos.ContextAsync(posCookie, cancellationToken) is not { } context)
        {
            return Error.Forbidden(Error.Codes.Forbidden, SaleReturnService.NotRegisterMessage);
        }

        if (input.SaleId == Guid.Empty || input.TenderId == Guid.Empty)
        {
            return SaleReturnService.Field("tenderId", "Informe a venda e o pagamento.");
        }

        var found = await FindAsync(input.Code, tracked: false, cancellationToken);
        if (found is null)
        {
            return Error.NotFound(Error.Codes.NotFound, NotFoundMessage);
        }

        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        var lockKey = LockKey(found.Id);
        await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))) AS l").ToListAsync(cancellationToken);
        var credit = await db.StoreCredits.FirstAsync(c => c.Id == found.Id, cancellationToken);
        if (credit.CodeHash != found.CodeHash)
        {
            return Error.NotFound(Error.Codes.NotFound, NotFoundMessage); // o código foi reemitido enquanto esperava a trava: o antigo não vale mais
        }

        // Reenvio do mesmo pagamento: devolve o mesmo abatimento (nunca abate duas vezes).
        if (await db.StoreCreditMovements.AsNoTracking().FirstOrDefaultAsync(m => m.TenderId == input.TenderId, cancellationToken) is { } existing)
        {
            return existing.StoreCreditId == credit.Id && existing.SaleId == input.SaleId && existing.Amount == input.Amount && existing.ReversedAt is null
                ? new StoreCreditRedeemed(credit.Id, existing.TenderId, existing.Amount, credit.Balance, credit.ExpiresOn)
                : SaleReturnService.Field("tenderId", "Este pagamento já usou outro vale ou outro valor.");
        }

        // Revisão de segurança (achado 4): a venda do caixa só existe no aparelho até ser concluída, então o abatimento fica preso ao caixa e à sessão
        // aberta: sem caixa aberto não abate; venda que já está no servidor tem de ser deste caixa e estar aberta; a mesma venda não abate em dois caixas.
        // O abatimento sem venda concluída volta ao vale depois de 24 h (ReleaseOrphansAsync, job por tenant).
        if (context.CashSessionId is null)
        {
            return Error.Validation("cash_session_closed", "Abra o caixa para usar o vale-troca.");
        }

        var serverSale = await db.Sales.AsNoTracking().Where(s => s.Id == input.SaleId).Select(s => new { s.RegisterId, s.Status }).FirstOrDefaultAsync(cancellationToken);
        if ((serverSale is not null && (serverSale.RegisterId != context.RegisterId || serverSale.Status is not (SaleStatus.Open or SaleStatus.PartiallyPaid)))
            || await db.StoreCreditMovements.AnyAsync(m => m.SaleId == input.SaleId && m.RegisterId != context.RegisterId, cancellationToken))
        {
            return SaleReturnService.Field("saleId", "O vale só abate em venda aberta deste caixa.");
        }

        if (ReturnRules.RedeemProblem(credit.Balance, credit.ExpiresOn, BusinessTime.Today(clock), input.Amount) is { } problem)
        {
            return Error.Validation("store_credit_refused", problem);
        }

        credit.Debit(input.Amount);
        db.StoreCreditMovements.Add(new StoreCreditMovement
        {
            StoreCreditId = credit.Id, TenderId = input.TenderId, SaleId = input.SaleId, Amount = input.Amount, OperatorId = operatorId, RegisterId = context.RegisterId,
            CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new StoreCreditRedeemed(credit.Id, input.TenderId, input.Amount, credit.Balance, credit.ExpiresOn);
    }

    /// <summary>Trava do vale (<c>pg_advisory_xact_lock</c>): o uso e a reemissão do código passam por ela.</summary>
    public static string LockKey(Guid storeCreditId) => "sales.store-credit:" + storeCreditId.ToString("N");

    /// <summary>
    /// Revisão de segurança (achado 4): abatimento órfão. O uso ainda não desfeito, feito há mais de <see cref="OrphanAfter"/>, cuja venda não chegou
    /// concluída ao servidor (não existe, ou foi cancelada), volta ao vale. A venda que subir depois com esse pagamento vai para a quarentena (o
    /// abatimento já está desfeito). Por tenant (job), idempotente.
    /// </summary>
    public async Task<int> ReleaseOrphansAsync(CancellationToken cancellationToken = default)
    {
        var limit = clock.GetUtcNow() - OrphanAfter;
        var orphans = await db.StoreCreditMovements.AsNoTracking()
            .Where(m => m.ReversedAt == null && m.CreatedAt < limit && !db.Sales.Any(s => s.Id == m.SaleId && ReturnRules.Done.Contains(s.Status)))
            .OrderBy(m => m.CreatedAt).Take(500).Select(m => new { m.Id, m.StoreCreditId }).ToListAsync(cancellationToken);
        foreach (var group in orphans.GroupBy(m => m.StoreCreditId))
        {
            await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
            var lockKey = LockKey(group.Key);
            await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))) AS l").ToListAsync(cancellationToken);
            var ids = group.Select(m => m.Id).ToList();
            // Relido sob a trava: o que já foi desfeito ou cuja venda chegou concluída nesse meio tempo fica como está.
            var fresh = await db.StoreCreditMovements
                .Where(m => ids.Contains(m.Id) && m.ReversedAt == null && !db.Sales.Any(s => s.Id == m.SaleId && ReturnRules.Done.Contains(s.Status))).ToListAsync(cancellationToken);
            await ReverseAsync(fresh, "Abatimento sem venda concluída em 24 h", cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }

        return orphans.Count;
    }

    public static readonly TimeSpan OrphanAfter = TimeSpan.FromHours(24);

    public async Task<StoreCreditRedemptionRef?> FindAsync(Guid tenderId, CancellationToken cancellationToken = default) =>
        await db.StoreCreditMovements.AsNoTracking().Where(m => m.TenderId == tenderId)
            .Select(m => new StoreCreditRedemptionRef(m.TenderId, m.SaleId, m.StoreCreditId, m.Amount, m.ReversedAt != null)).FirstOrDefaultAsync(cancellationToken);

    /// <summary>Venda concluída: o abatimento sem pagamento capturado na venda volta ao vale; a troca liga a venda nova à devolução.</summary>
    public async Task HandleAsync(SaleCompleted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var uses = await db.StoreCreditMovements.Where(m => m.SaleId == integrationEvent.SaleId && m.ReversedAt == null).ToListAsync(cancellationToken);
        if (uses.Count == 0)
        {
            return;
        }

        var captured = await db.SalePayments.AsNoTracking().Where(p => p.SaleId == integrationEvent.SaleId && p.Status == SalePaymentStatus.Captured).Select(p => p.Id)
            .ToListAsync(cancellationToken);
        await ReverseAsync(uses.Where(u => !captured.Contains(u.TenderId)).ToList(), "Pagamento com vale desfeito na venda " + integrationEvent.Number, cancellationToken);
        var creditIds = uses.Where(u => captured.Contains(u.TenderId)).Select(u => u.StoreCreditId).Distinct().ToList();
        var origins = await db.StoreCredits.AsNoTracking().Where(c => creditIds.Contains(c.Id)).Select(c => c.OriginReturnId).ToListAsync(cancellationToken);
        foreach (var exchange in await db.SaleReturns.Where(r => origins.Contains(r.Id) && r.IsExchange && r.ExchangeSaleId == null).ToListAsync(cancellationToken))
        {
            exchange.LinkExchange(integrationEvent.SaleId);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task HandleAsync(SaleCancelled integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        await ReverseAllAsync(integrationEvent.SaleId, "Venda " + integrationEvent.Number + " cancelada", cancellationToken);
    }

    public async Task HandleAsync(PosSaleDiscarded integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        await ReverseAllAsync(integrationEvent.SaleId, "Venda " + integrationEvent.Number + " descartada no caixa", cancellationToken);
    }

    private async Task ReverseAllAsync(Guid saleId, string reason, CancellationToken ct)
    {
        var uses = await db.StoreCreditMovements.Where(m => m.SaleId == saleId && m.ReversedAt == null).ToListAsync(ct);
        await ReverseAsync(uses, reason, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>O valor volta ao vale (uma vez por uso, idempotente pela marca do desfazer).</summary>
    private async Task ReverseAsync(List<StoreCreditMovement> uses, string reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        foreach (var use in uses)
        {
            var credit = await db.StoreCredits.FirstAsync(c => c.Id == use.StoreCreditId, ct);
            credit.Credit(use.Amount);
            use.ReversedAt = now;
            use.ReversalReason = reason.Length <= 200 ? reason : reason[..200];
        }
    }

    private async Task<StoreCredit?> FindAsync(string? code, bool tracked, CancellationToken ct)
    {
        if (ReturnRules.Normalize(code) is not { } normalized)
        {
            return null;
        }

        var hash = ReturnRules.Hash(normalized);
        var query = tracked ? db.StoreCredits : db.StoreCredits.AsNoTracking();
        return await query.FirstOrDefaultAsync(c => c.CodeHash == hash, ct);
    }
}

public sealed record ReleaseStoreCreditOrphansArgs;

/// <summary>Revisão de segurança da PDV-06 (achado 4): abatimentos de vale sem venda concluída em 24 h voltam ao vale, por tenant.</summary>
public sealed class ReleaseStoreCreditOrphansJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<ReleaseStoreCreditOrphansJob> logger)
    : TenantJob<ReleaseStoreCreditOrphansArgs>(directory, scopes, logger)
{
    protected override Task RunAsync(IServiceProvider services, TenantInfo tenant, ReleaseStoreCreditOrphansArgs args, CancellationToken cancellationToken) =>
        services.GetRequiredService<StoreCreditService>().ReleaseOrphansAsync(cancellationToken);
}

/// <summary>Recorrente global de hora em hora que enfileira a devolução dos abatimentos órfãos por tenant ativo.</summary>
public sealed class ReleaseStoreCreditOrphansRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<ReleaseStoreCreditOrphansJob, ReleaseStoreCreditOrphansArgs>(directory, jobs)
{
    public const string Id = "sales-store-credit-orphans";
    public const string Cron = "17 * * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override ReleaseStoreCreditOrphansArgs ArgsFor(TenantInfo tenant) => new();
}
