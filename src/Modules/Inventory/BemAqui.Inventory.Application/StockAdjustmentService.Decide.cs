using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

public sealed partial class StockAdjustmentService
{
    /// <summary>
    /// "Aprovar ajuste" (CA 5 a 8, 11 e 18): na mesma transação o ajuste fica <c>Approved</c> e cada linha vira um movimento
    /// <c>Adjustment</c> síncrono pelo motor (sobra entra e falta sai pelo custo médio do momento). O servidor recalcula sobre o saldo
    /// de agora (modo Counted) ou mantém a diferença (modo Delta) e confere o reservado de novo: linha que ficaria negativa ou abaixo do
    /// reservado responde 409 <c>insufficient_stock</c> e nada é gravado. A decisão grava antes do motor: duas aprovações simultâneas
    /// param no <c>xmin</c>.
    /// </summary>
    public async Task<Result<StockAdjustmentDetail>> ApproveAsync(Guid id, StockAdjustmentDecisionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var comment = await DecisionGuardAsync(input, requireComment: false, cancellationToken);
        if (comment.IsFailure)
        {
            return comment.Error!;
        }

        var pending = await PendingAsync(id, input.Version, cancellationToken);
        if (pending.IsFailure)
        {
            return pending.Error!;
        }

        var adjustment = pending.Value;
        var me = currentUser.UserId;
        var self = false;
        if (me is { } actor && adjustment.RequestedBy == actor)
        {
            if (!StockAdjustmentRules.MayDecide(adjustment.RequestedBy, actor, await IsDecidingAloneAsync(actor, cancellationToken)))
            {
                return Error.Forbidden("own_stock_adjustment", "Quem lança o ajuste não o aprova.");
            }

            self = true;
        }

        if (await locks.CheckAsync([adjustment.LocationId], cancellationToken) is { } blocked)
        {
            return blocked;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Entry(adjustment).Property(a => a.Version).OriginalValue = input.Version;
        adjustment.Status = StockAdjustmentStatus.Approved;
        adjustment.DecidedBy = me;
        adjustment.DecidedAt = clock.GetUtcNow();
        adjustment.DecisionComment = comment.Value;
        adjustment.SelfApproved = self;
        // Grava a decisão antes do motor: a segunda aprovação simultânea para no xmin (CA 11).
        await db.SaveChangesAsync(cancellationToken);

        var lines = await db.StockAdjustmentLines.Where(l => l.AdjustmentId == id).OrderBy(l => l.Sequence).ToListAsync(cancellationToken);
        foreach (var itemId in lines.Select(l => l.ItemId).Distinct().Order())
        {
            // Trava os itens antes de ler o saldo (ADR 0023 §3): a conta usa o saldo que o motor vai mexer.
            await allocator.LockItemAsync(itemId, cancellationToken);
        }

        var balances = await BalancesAsync(adjustment.LocationId, lines.Select(l => (l.ItemId, l.LotId)).ToList(), cancellationToken);
        var units = await UnitCodesAsync(lines.Select(l => l.ItemId), cancellationToken);
        var inputs = lines.Select(l => new AdjustmentLineInput(l.ItemId, l.LotId, l.CountedQty, l.DeltaQty)).ToList();
        var (results, refusal) = StockAdjustmentRules.Evaluate(adjustment.Mode, inputs, balances, lines.Select(l => units.GetValueOrDefault(l.ItemId) ?? string.Empty).ToList());
        if (refusal is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            var line = lines[refusal.Index];
            var data = ReservationsData(line.ItemId, adjustment.LocationId, refusal);
            data["lineId"] = line.Id;
            data["field"] = $"lines[{refusal.Index}].quantity";
            return Error.Conflict(StockLedger.InsufficientStockCode, $"Linha {line.Sequence}: {refusal.Message}") with { Data = data };
        }

        var reason = StockAdjustmentRules.MovementReason(adjustment.Number, adjustment.Reason, adjustment.ReasonDetail);
        var moving = Enumerable.Range(0, lines.Count).Where(i => results[i].Delta != 0).ToList();
        var postings = moving.Select(i => new StockPosting(
            StockMovementType.Adjustment, lines[i].ItemId, adjustment.LocationId, lines[i].LotId, results[i].Delta, null, StockSources.Adjustment, adjustment.Id, lines[i].Sequence,
            Reason: reason, SourceNumber: adjustment.Number)).ToList();
        var posted = await ledger.PostManyAsync(postings, cancellationToken);
        if (posted.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            return posted.Error!;
        }

        var movementIds = posted.Value.Select(p => p.MovementId).ToList();
        var costs = await db.StockMovements.AsNoTracking().Where(m => movementIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.UnitCost, cancellationToken);
        for (var i = 0; i < lines.Count; i++)
        {
            lines[i].SystemQtyAtApproval = results[i].SystemQty;
            lines[i].AppliedDeltaQty = results[i].Delta;
        }

        for (var k = 0; k < moving.Count; k++)
        {
            var line = lines[moving[k]];
            line.MovementId = posted.Value[k].MovementId;
            line.UnitCost = costs.GetValueOrDefault(line.MovementId.Value);
        }

        (adjustment.SurplusValue, adjustment.ShortageValue) = StockAdjustmentRules.Totals(lines.Select(l => (l.AppliedDeltaQty ?? 0m, l.UnitCost ?? 0m)));
        if (self)
        {
            await NotifySelfApprovedAsync($"aprovou o próprio ajuste {adjustment.Number}", Link + adjustment.Id, cancellationToken);
        }
        else if (adjustment.RequestedBy is { } author)
        {
            notifications.Notify(author, DecisionType, $"Ajuste {adjustment.Number} aprovado.", Link + adjustment.Id);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(id, cancellationToken);
    }

    /// <summary>"Reprovar ajuste" (CA 9): comentário obrigatório; fica <c>Rejected</c>, nenhum movimento nasce e o autor recebe o comentário.</summary>
    public async Task<Result<StockAdjustmentDetail>> RejectAsync(Guid id, StockAdjustmentDecisionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var comment = await DecisionGuardAsync(input, requireComment: true, cancellationToken);
        if (comment.IsFailure)
        {
            return comment.Error!;
        }

        var pending = await PendingAsync(id, input.Version, cancellationToken);
        if (pending.IsFailure)
        {
            return pending.Error!;
        }

        var adjustment = pending.Value;
        if (currentUser.UserId is { } me && adjustment.RequestedBy == me)
        {
            return Error.Forbidden("own_stock_adjustment", "Para desistir do próprio ajuste, use \"Cancelar ajuste\".");
        }

        db.Entry(adjustment).Property(a => a.Version).OriginalValue = input.Version;
        adjustment.Status = StockAdjustmentStatus.Rejected;
        adjustment.DecidedBy = currentUser.UserId;
        adjustment.DecidedAt = clock.GetUtcNow();
        adjustment.DecisionComment = comment.Value;
        if (adjustment.RequestedBy is { } author)
        {
            notifications.Notify(author, DecisionType, $"Ajuste {adjustment.Number} reprovado: {Cut(comment.Value!, 120)}", Link + adjustment.Id);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(id, cancellationToken);
    }

    /// <summary>"Cancelar ajuste" (CA 10): só o autor, enquanto pendente, com motivo; fica <c>Cancelled</c>, sem movimento.</summary>
    public async Task<Result<StockAdjustmentDetail>> CancelAsync(Guid id, StockAdjustmentReasonInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var reason = Clean(input.Reason);
        if (reason is null || reason.Length > StockAdjustmentRules.MaxReasonDetail)
        {
            return Invalid("reason", $"Informe o motivo do cancelamento, com até {StockAdjustmentRules.MaxReasonDetail} caracteres.");
        }

        var pending = await PendingAsync(id, input.Version, cancellationToken);
        if (pending.IsFailure)
        {
            return pending.Error!;
        }

        var adjustment = pending.Value;
        if (currentUser.UserId is not { } me || adjustment.RequestedBy != me)
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Só quem lançou cancela o ajuste.");
        }

        db.Entry(adjustment).Property(a => a.Version).OriginalValue = input.Version;
        adjustment.Status = StockAdjustmentStatus.Cancelled;
        adjustment.CancelReason = reason;
        adjustment.DecidedBy = me;
        adjustment.DecidedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(id, cancellationToken);
    }

    /// <summary>
    /// "Estornar ajuste" (CA 12 e 18): pelo próprio documento, com <c>inventory.approve</c>, por quem não lançou (salvo o Proprietário
    /// único), com motivo; nascem os movimentos inversos (EST-01a CA 21), com um número MOV novo. Sobra já consumida ou reservada: 409
    /// <c>insufficient_stock</c> e nada é gravado.
    /// </summary>
    public async Task<Result<StockAdjustmentDetail>> ReverseAsync(Guid id, StockAdjustmentReasonInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var reason = Clean(input.Reason);
        if (reason is null || reason.Length > StockAdjustmentRules.MaxReasonDetail)
        {
            return Invalid("reason", $"Informe o motivo do estorno, com até {StockAdjustmentRules.MaxReasonDetail} caracteres.");
        }

        if (!await permissions.HasPermissionAsync("inventory", "approve", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Quem estorna o ajuste é quem aprova no Estoque.");
        }

        var adjustment = await db.StockAdjustments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (adjustment is null)
        {
            return NotFound();
        }

        if (adjustment.Status != StockAdjustmentStatus.Approved)
        {
            return Error.Conflict("stock_adjustment_not_approved", "Só o ajuste aprovado se estorna.");
        }

        if (adjustment.ReversedAt is not null)
        {
            return Error.Conflict("stock_adjustment_reversed", "Este ajuste já foi estornado.");
        }

        if (adjustment.Version != input.Version)
        {
            return Error.Conflict("concurrency_conflict", "O ajuste mudou desde que você abriu. Recarregue.");
        }

        var me = currentUser.UserId;
        var self = false;
        if (me is { } actor && adjustment.RequestedBy == actor)
        {
            if (!StockAdjustmentRules.MayDecide(adjustment.RequestedBy, actor, await IsDecidingAloneAsync(actor, cancellationToken)))
            {
                return Error.Forbidden("own_stock_adjustment", "Quem lança o ajuste não o estorna.");
            }

            self = true;
        }

        if (await locks.CheckAsync([adjustment.LocationId], cancellationToken) is { } blocked)
        {
            return blocked;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Entry(adjustment).Property(a => a.Version).OriginalValue = input.Version;
        adjustment.ReversedAt = clock.GetUtcNow();
        adjustment.ReversedBy = me;
        adjustment.ReversalReason = reason;
        adjustment.ReversalSelfApproved = self;
        // Grava antes do motor: dois estornos simultâneos param no xmin.
        await db.SaveChangesAsync(cancellationToken);
        var lines = await db.StockAdjustmentLines.AsNoTracking().Where(l => l.AdjustmentId == id && l.MovementId != null).ToListAsync(cancellationToken);
        string? number = null;
        // Em ordem crescente de item (ADR 0023 §3), como a aprovação.
        foreach (var line in lines.OrderBy(l => l.ItemId).ThenBy(l => l.Sequence))
        {
            var reversed = await ledger.ReverseAsync(new StockReversal(line.MovementId!.Value, StockSources.Adjustment, adjustment.Id, $"Estorno do ajuste {adjustment.Number}: {reason}", number), cancellationToken);
            if (reversed.IsFailure)
            {
                await transaction.RollbackAsync(cancellationToken);
                return reversed.Error!;
            }

            number ??= reversed.Value.Number;
        }

        if (self)
        {
            await NotifySelfApprovedAsync($"estornou o próprio ajuste {adjustment.Number}", Link + adjustment.Id, cancellationToken);
        }
        else if (adjustment.RequestedBy is { } author)
        {
            notifications.Notify(author, DecisionType, $"Ajuste {adjustment.Number} estornado: {Cut(reason, 120)}", Link + adjustment.Id);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(id, cancellationToken);
    }

    /// <summary>
    /// Editar o ajuste (CA 12): nunca. O aprovado responde 409 com o caminho (outro ajuste ou o estorno); o pendente é cancelado pelo
    /// autor e lançado de novo, e quem aprova reprova com o comentário (CA 6).
    /// </summary>
    public async Task<Result> EditAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var status = await db.StockAdjustments.AsNoTracking().Where(a => a.Id == id).Select(a => (StockAdjustmentStatus?)a.Status).FirstOrDefaultAsync(cancellationToken);
        return status switch
        {
            null => NotFound(),
            StockAdjustmentStatus.Approved => Error.Conflict("stock_adjustment_decided", "Ajuste aprovado não muda. Para corrigir, lance outro ajuste ou estorne este."),
            _ => Error.Conflict("stock_adjustment_not_editable", "O ajuste enviado não se edita: quem lançou cancela e lança outro; quem aprova reprova com o comentário."),
        };
    }

    /// <summary>Só a decisão e o comentário (CA 6); aprovar e reprovar exigem <c>inventory.approve</c>.</summary>
    private async Task<Result<string?>> DecisionGuardAsync(StockAdjustmentDecisionInput input, bool requireComment, CancellationToken ct)
    {
        if (input.Extra is { Count: > 0 } extra)
        {
            const string message = "Quem aprova decide sobre o que foi enviado: envie só a decisão e o comentário.";
            return Error.Validation("decision_only", message, extra.Keys.ToDictionary(k => k, _ => new[] { message }));
        }

        if (!await permissions.HasPermissionAsync("inventory", "approve", ct))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Quem decide o ajuste é quem aprova no Estoque.");
        }

        var comment = Clean(input.Comment);
        if (requireComment && comment is null)
        {
            return Invalid("comment", "Escreva o motivo da reprovação.");
        }

        return comment is { Length: > StockAdjustmentRules.MaxComment } ? Invalid("comment", $"Escreva o comentário com até {StockAdjustmentRules.MaxComment} caracteres.") : comment;
    }

    private async Task<Result<StockAdjustment>> PendingAsync(Guid id, uint version, CancellationToken ct)
    {
        var adjustment = await db.StockAdjustments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (adjustment is null)
        {
            return NotFound();
        }

        if (adjustment.Status != StockAdjustmentStatus.Pending)
        {
            return Error.Conflict("stock_adjustment_decided", "O ajuste já foi decidido.");
        }

        return adjustment.Version != version ? Error.Conflict("concurrency_conflict", "O ajuste mudou desde que você abriu. Recarregue.") : adjustment;
    }

    private async Task<Dictionary<Guid, string>> UnitCodesAsync(IEnumerable<Guid> itemIds, CancellationToken ct)
    {
        var ids = itemIds.Distinct().ToList();
        return await (from i in db.Items.IgnoreQueryFilters().AsNoTracking()
                      join u in db.Units.IgnoreQueryFilters() on i.UnitId equals u.Id
                      where ids.Contains(i.Id)
                      select new { i.Id, u.Code }).ToDictionaryAsync(x => x.Id, x => x.Code, ct);
    }

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
