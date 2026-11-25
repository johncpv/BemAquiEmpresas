using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Identity.Contracts;
using BemAqui.Pos.Contracts;
using BemAqui.Pos.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Pos.Application;

/// <summary>
/// Apoio do <c>Pos</c> à devolução do <c>Sales</c> (PDV-06, <see cref="IPosReturnSupport"/>): o caixa do aparelho, a sessão aberta e o dinheiro dela, a
/// aprovação de supervisor da devolução e o movimento de dinheiro <c>Refund</c>. Escreve só no rastreamento: quem chama grava, na mesma transação.
/// </summary>
public sealed class PosReturnSupport(IPosDb db, IPosRegisterDevice devices, IEventPublisher events, TimeProvider clock) : IPosReturnSupport
{
    public async Task<PosReturnContext?> ContextAsync(string? posCookie, CancellationToken cancellationToken = default)
    {
        if (await devices.ResolveAsync(posCookie, cancellationToken) is not { } device)
        {
            return null;
        }

        var register = await db.Registers.AsNoTracking().FirstOrDefaultAsync(r => r.Id == device.RegisterId && r.StoreId == device.StoreId, cancellationToken);
        var store = await db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == device.StoreId, cancellationToken);
        if (register is null || store is null || !register.IsActive || !store.IsActive)
        {
            return null;
        }

        // Sessão aberta do caixa, ainda sem "Fechar caixa" (CX-02: depois dele nada entra na sessão).
        var session = await db.CashSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.RegisterId == register.Id && s.Status == CashSessionStatus.Open && s.CountStartedAt == null, cancellationToken);
        var cash = session is null ? 0m : await CashInDrawerAsync(session, cancellationToken);
        return new PosReturnContext(store.Id, store.Name, register.Id, register.Number, device.DeviceId, store.StockLocationId, store.ExchangeDays, store.StoreCreditValidityDays,
            session?.Id, cash);
    }

    public async Task<Result<Guid>> UseApprovalAsync(Guid approvalId, Guid operatorId, Guid registerId, Guid returnId, decimal amount, CancellationToken cancellationToken = default)
    {
        var approval = await db.SupervisorApprovals.FirstOrDefaultAsync(a => a.Id == approvalId, cancellationToken);
        var now = clock.GetUtcNow();
        if (approval is null || approval.Action != SupervisorAction.Return || approval.SaleId != returnId || approval.OperatorId != operatorId || approval.RegisterId != registerId
            || approval.BeforeAmount != amount || approval.UsedAt is not null)
        {
            return Error.Validation("return_approval_invalid", "A aprovação do supervisor não confere com esta devolução. Chame o supervisor de novo.");
        }

        if (now - approval.ApprovedAt > SupervisorApproval.MaxUseDelay)
        {
            return Error.Validation("return_approval_expired", "A aprovação do supervisor venceu. Chame o supervisor de novo.");
        }

        approval.UseForReturn(returnId, now);
        return approval.SupervisorId;
    }

    public async Task RecordCashRefundAsync(Guid sessionId, Guid returnId, string returnNumber, decimal amount, Guid operatorId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        if (await db.CashMovements.AnyAsync(m => m.Id == returnId, cancellationToken))
        {
            return;
        }

        var session = await db.CashSessions.AsNoTracking().FirstAsync(s => s.Id == sessionId, cancellationToken);
        var register = await db.Registers.AsNoTracking().FirstAsync(r => r.Id == session.RegisterId, cancellationToken);
        var movement = CashMovement.FromDevice(returnId, sessionId, CashMovementKind.Refund, amount, "Devolução " + returnNumber, operatorId, at, false, clock.GetUtcNow());
        db.CashMovements.Add(movement);
        events.Publish(new CashMovementRecorded(
            movement.Id, sessionId, register.StoreId, register.Id, register.Number, register.CashAccountId, CashMovementRecorded.Refund, amount,
            DateOnly.FromDateTime(BusinessTime.ToBusiness(at).DateTime), at));
    }

    /// <summary>Dinheiro na gaveta agora: fundo de troco + dinheiro das vendas da sessão + o efeito de cada movimento (suprimento, sangria, devolução).</summary>
    private async Task<decimal> CashInDrawerAsync(CashSession session, CancellationToken ct)
    {
        var sales = await db.SessionSales.AsNoTracking().Where(s => s.SessionId == session.Id).SumAsync(s => s.CashAmount, ct);
        var movements = await db.CashMovements.AsNoTracking().Where(m => m.SessionId == session.Id && m.Kind != CashMovementKind.Opening)
            .Select(m => new { m.Kind, m.Amount }).ToListAsync(ct);
        return session.OpeningAmount + sales + movements.Sum(m => CashMovement.DrawerEffect(m.Kind, m.Amount));
    }
}
