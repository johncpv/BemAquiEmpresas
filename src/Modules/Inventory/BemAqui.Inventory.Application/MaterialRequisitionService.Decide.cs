using BemAqui.BuildingBlocks.Authorization;
using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

public sealed partial class MaterialRequisitionService
{
    /// <summary>
    /// "Aprovar requisição" (CA 4): o aprovador decide sobre o que foi enviado (campo a mais é 422) e a baixa segue o CA 2 com o saldo do
    /// momento. Quem enviou não aprova, salvo o Proprietário único ("Autoaprovado" e aviso ao Proprietário).
    /// </summary>
    public async Task<Result<RequisitionDetail>> ApproveAsync(Guid id, RequisitionDecisionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var comment = await DecisionGuardAsync(input, requireComment: false, cancellationToken);
        if (comment.IsFailure)
        {
            return comment.Error!;
        }

        var found = await InStatusAsync(id, MaterialRequisitionStatus.PendingApproval, input.Version, "A requisição já foi decidida.", cancellationToken);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var requisition = found.Value;
        var order = await OpenOrderAsync(requisition.ProductionOrderId, cancellationToken);
        if (order.IsFailure)
        {
            return order.Error!;
        }

        var me = currentUser.UserId;
        var author = requisition.CompletedBy ?? requisition.RequestedBy;
        var self = false;
        if (me is { } actor && author == actor)
        {
            if (!await SoleOwnerRule.NoOtherEligibleAsync(users, "inventory.approve", actor, [actor], cancellationToken))
            {
                return Error.Forbidden("own_requisition", "Quem envia a requisição não aprova a própria.");
            }

            self = true;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await reservations.LockOrderAsync(requisition.ProductionOrderId, cancellationToken);
        db.Entry(requisition).Property(r => r.Version).OriginalValue = input.Version;
        requisition.DecidedBy = me;
        requisition.DecidedAt = clock.GetUtcNow();
        requisition.DecisionComment = comment.Value;
        requisition.SelfApproved = self;
        // Grava a decisão antes do motor: duas aprovações simultâneas param no xmin e só uma segue (CA 16).
        requisition.Status = MaterialRequisitionStatus.Completed;
        await db.SaveChangesAsync(cancellationToken);
        var lines = await db.MaterialRequisitionLines.Where(l => l.RequisitionId == id).OrderBy(l => l.Sequence).ToListAsync(cancellationToken);
        var executed = await ExecuteIssueAsync(requisition, lines, evaluateApproval: false, null, cancellationToken);
        if (executed.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            return executed.Error!;
        }

        if (self)
        {
            await NotifySelfApprovedAsync($"aprovou a própria requisição {requisition.Number}", Link + requisition.Id, cancellationToken);
        }
        else if (author is { } who)
        {
            notifications.Notify(who, DecisionType, $"Requisição {requisition.Number} aprovada: material baixado.", Link + requisition.Id);
        }

        await NotifyRequesterAsync(requisition, $"Material separado: {requisition.Number}.", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(id, cancellationToken);
    }

    /// <summary>"Reprovar requisição" (CA 4): comentário obrigatório; fica <c>Rejected</c> sem movimento e o autor é avisado.</summary>
    public async Task<Result<RequisitionDetail>> RejectAsync(Guid id, RequisitionDecisionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var comment = await DecisionGuardAsync(input, requireComment: true, cancellationToken);
        if (comment.IsFailure)
        {
            return comment.Error!;
        }

        var found = await InStatusAsync(id, MaterialRequisitionStatus.PendingApproval, input.Version, "A requisição já foi decidida.", cancellationToken);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var requisition = found.Value;
        var author = requisition.CompletedBy ?? requisition.RequestedBy;
        if (currentUser.UserId is { } me && author == me)
        {
            return Error.Forbidden("own_requisition", "Quem envia a requisição não reprova a própria.");
        }

        db.Entry(requisition).Property(r => r.Version).OriginalValue = input.Version;
        requisition.Status = MaterialRequisitionStatus.Rejected;
        requisition.DecidedBy = currentUser.UserId;
        requisition.DecidedAt = clock.GetUtcNow();
        requisition.DecisionComment = comment.Value;
        if (author is { } who)
        {
            notifications.Notify(who, DecisionType, $"Requisição {requisition.Number} reprovada: {Cut(comment.Value!, 120)}", Link + requisition.Id);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(id, cancellationToken);
    }

    /// <summary>
    /// Confirmar o pedido do operador (CA 7): o almoxarife ou o supervisor ajusta lote e quantidade e baixa pelo CA 2 (com o CA 3 e o
    /// CA 4 valendo). A confirmação simultânea do mesmo pedido para no xmin (CA 16): só uma baixa.
    /// </summary>
    public async Task<Result<RequisitionDetail>> ConfirmAsync(Guid id, RequisitionConfirmInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var found = await InStatusAsync(id, MaterialRequisitionStatus.Requested, input.Version, "O pedido já foi atendido ou recusado.", cancellationToken);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var requisition = found.Value;
        var order = await OpenOrderAsync(requisition.ProductionOrderId, cancellationToken);
        if (order.IsFailure)
        {
            return order.Error!;
        }

        var valid = await ValidateLinesAsync(requisition.ProductionOrderId, input.Lines, cancellationToken);
        if (valid.IsFailure)
        {
            return valid.Error!;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await reservations.LockOrderAsync(requisition.ProductionOrderId, cancellationToken);
        db.Entry(requisition).Property(r => r.Version).OriginalValue = input.Version;
        requisition.Status = MaterialRequisitionStatus.Completed;
        requisition.CompletedBy = currentUser.UserId;
        await db.SaveChangesAsync(cancellationToken);
        // O pedido vira o que foi separado: as linhas do operador saem (ficam na auditoria) e entram as confirmadas.
        var old = await db.MaterialRequisitionLines.Where(l => l.RequisitionId == id).ToListAsync(cancellationToken);
        var next = old.Count == 0 ? 1 : old.Max(l => l.Sequence) + 1;
        foreach (var line in old)
        {
            db.Entry(line).State = EntityState.Deleted;
        }

        var lines = AddLines(requisition, valid.Value, next);
        var executed = await ExecuteIssueAsync(requisition, lines, evaluateApproval: true, order.Value.Number, cancellationToken);
        if (executed.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            return executed.Error!;
        }

        if (requisition.Status == MaterialRequisitionStatus.Completed)
        {
            await NotifyRequesterAsync(requisition, $"Material separado: {requisition.Number} da {order.Value.Number}.", cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(id, cancellationToken);
    }

    /// <summary>"Recusar pedido" (CA 7): motivo obrigatório; o operador vê "Recusado: {motivo}" no <c>/fab</c> e recebe o aviso.</summary>
    public async Task<Result<RequisitionDetail>> DeclineAsync(Guid id, RequisitionDeclineInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var reason = Clean(input.Reason);
        if (reason is null || reason.Length > RequisitionRules.MaxReason)
        {
            return Invalid("reason", $"Escreva o motivo da recusa, com até {RequisitionRules.MaxReason} caracteres.");
        }

        var found = await InStatusAsync(id, MaterialRequisitionStatus.Requested, input.Version, "O pedido já foi atendido ou recusado.", cancellationToken);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var requisition = found.Value;
        db.Entry(requisition).Property(r => r.Version).OriginalValue = input.Version;
        requisition.Status = MaterialRequisitionStatus.Rejected;
        requisition.RejectReason = reason;
        requisition.DecidedBy = currentUser.UserId;
        requisition.DecidedAt = clock.GetUtcNow();
        await NotifyRequesterAsync(requisition, $"Pedido de material {requisition.Number} recusado: {Cut(reason, 120)}", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(id, cancellationToken);
    }

    /// <summary>Aviso ao operador que pediu (CA 7), com link para a etapa no <c>/fab</c>.</summary>
    private Task NotifyRequesterAsync(MaterialRequisition requisition, string title, CancellationToken ct)
    {
        if (requisition.StepId is { } step && requisition.RequestedBy is { } requester && requester != currentUser.UserId)
        {
            notifications.Notify(requester, DecisionType, title, "/fab/etapa/" + step);
        }

        return Task.CompletedTask;
    }

    /// <summary>Só a decisão e o comentário (CA 4); aprovar e reprovar exigem <c>inventory.approve</c>.</summary>
    private async Task<Result<string?>> DecisionGuardAsync(RequisitionDecisionInput input, bool requireComment, CancellationToken ct)
    {
        if (input.Extra is { Count: > 0 } extra)
        {
            const string message = "Quem aprova decide sobre o que foi enviado: envie só a decisão e o comentário.";
            return Error.Validation("decision_only", message, extra.Keys.ToDictionary(k => k, _ => new[] { message }));
        }

        if (!await permissions.HasPermissionAsync("inventory", "approve", ct))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Quem decide a requisição é quem aprova no Estoque.");
        }

        var comment = Clean(input.Comment);
        if (requireComment && comment is null)
        {
            return Invalid("comment", "Escreva o motivo da reprovação.");
        }

        return comment is { Length: > RequisitionRules.MaxComment } ? Invalid("comment", $"Escreva o comentário com até {RequisitionRules.MaxComment} caracteres.") : comment;
    }

    private async Task<Result<MaterialRequisition>> InStatusAsync(Guid id, MaterialRequisitionStatus status, uint version, string decided, CancellationToken ct)
    {
        var requisition = await db.MaterialRequisitions.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (requisition is null)
        {
            return NotFound();
        }

        if (requisition.Status != status)
        {
            return Error.Conflict("requisition_decided", decided);
        }

        return requisition.Version != version ? Error.Conflict("concurrency_conflict", "A requisição mudou desde que você abriu. Recarregue.") : requisition;
    }

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
