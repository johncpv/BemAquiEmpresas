using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Pos.Contracts;
using BemAqui.Sales.Contracts;
using BemAqui.Sales.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

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
        await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({"sales.store-credit:" + found.Id.ToString("N")}, 0))) AS l")
            .ToListAsync(cancellationToken);
        var credit = await db.StoreCredits.FirstAsync(c => c.Id == found.Id, cancellationToken);
        // Reenvio do mesmo pagamento: devolve o mesmo abatimento (nunca abate duas vezes).
        if (await db.StoreCreditMovements.AsNoTracking().FirstOrDefaultAsync(m => m.TenderId == input.TenderId, cancellationToken) is { } existing)
        {
            return existing.StoreCreditId == credit.Id && existing.SaleId == input.SaleId && existing.Amount == input.Amount && existing.ReversedAt is null
                ? new StoreCreditRedeemed(credit.Id, existing.TenderId, existing.Amount, credit.Balance, credit.ExpiresOn)
                : SaleReturnService.Field("tenderId", "Este pagamento já usou outro vale ou outro valor.");
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
