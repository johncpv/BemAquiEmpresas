using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application.TimeClock;

public sealed partial class TimeAdjustmentService
{
    /// <summary>
    /// "Aprovar ajuste" (CA 11, 13, 14 e 16), com <c>hr.approve</c>: na mesma transação o pedido fica <c>Approved</c> (gravado antes do efeito:
    /// a segunda decisão simultânea para no <c>xmin</c>, 409) e "Incluir" cria a marcação <c>Manual</c> com quem aprovou e o pedido;
    /// "Desconsiderar" passa a original a <c>Voided</c> (nada é apagado); o abono passa a valer para o espelho. O solicitante é avisado.
    /// </summary>
    public async Task<Result<TimeAdjustmentView>> ApproveAsync(Guid id, TimeAdjustmentDecisionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var pending = await PendingAsync(id, input.Version, cancellationToken);
        if (pending.IsFailure)
        {
            return pending.Error!;
        }

        var adjustment = pending.Value;
        var me = currentUser.UserId;
        var self = false;
        if (me is { } actor && await InvolvedAsync(adjustment, actor, cancellationToken))
        {
            if (!TimeAdjustmentRules.MayDecide([actor], actor, await SoleApproverAsync(actor, cancellationToken)))
            {
                return Error.Forbidden(OwnCode, TimeAdjustmentRules.OwnMessage);
            }

            self = true;
        }

        if (await periods.CheckAsync(adjustment.Day, cancellationToken) is { } closed)
        {
            return closed;
        }

        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Entry(adjustment).Property(a => a.Version).OriginalValue = input.Version;
        adjustment.Status = TimeAdjustmentStatus.Approved;
        adjustment.DecidedBy = me;
        adjustment.DecidedAt = now;
        adjustment.SelfApproved = self;
        await db.SaveChangesAsync(cancellationToken);

        switch (adjustment.Kind)
        {
            case TimeAdjustmentKind.AddEntry:
                var at = adjustment.At!.Value;
                var entry = new TimeEntry
                {
                    EmployeeId = adjustment.EmployeeId, At = at, Day = DateOnly.FromDateTime(BusinessTime.ToBusiness(at).DateTime), Type = adjustment.EntryType!.Value,
                    Source = TimeEntrySource.Manual, RecordedByUserId = adjustment.RequestedBy, ApprovedBy = me, AdjustmentId = adjustment.Id, CreatedAt = now,
                };
                db.TimeEntries.Add(entry);
                adjustment.CreatedEntryId = entry.Id;
                break;
            case TimeAdjustmentKind.VoidEntry:
                var original = await db.TimeEntries.FirstOrDefaultAsync(e => e.Id == adjustment.EntryId && e.EmployeeId == adjustment.EmployeeId, cancellationToken);
                if (original is null || original.Status != TimeEntryStatus.Valid)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    db.ChangeTracker.Clear();
                    return Error.Conflict("time_entry_voided", "Esta marcação já foi desconsiderada por outro ajuste.");
                }

                original.Status = TimeEntryStatus.Voided;
                original.VoidAdjustmentId = adjustment.Id;
                original.VoidedBy = me;
                original.VoidedAt = now;
                break;
        }

        if (adjustment.RequestedBy is { } requester && requester != me)
        {
            notifications.Notify(requester, DecidedType, TimeAdjustmentRules.ApprovedNotice(adjustment.Day), RequesterLink(adjustment));
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await ViewAsync(id, cancellationToken);
    }

    /// <summary>"Reprovar" (CA 12 e 16): comentário obrigatório; nada muda nas marcações e o solicitante recebe o comentário.</summary>
    public async Task<Result<TimeAdjustmentView>> RejectAsync(Guid id, TimeAdjustmentDecisionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var comment = input.Comment?.Trim();
        if (string.IsNullOrEmpty(comment) || comment.Length > TimeAdjustmentRules.CommentMax)
        {
            return Invalid("comment", $"Escreva o motivo da reprovação, com até {TimeAdjustmentRules.CommentMax} caracteres.");
        }

        var pending = await PendingAsync(id, input.Version, cancellationToken);
        if (pending.IsFailure)
        {
            return pending.Error!;
        }

        var adjustment = pending.Value;
        if (currentUser.UserId is { } me && adjustment.RequestedBy == me)
        {
            return Error.Forbidden(OwnCode, "Para desistir do próprio pedido, use \"Cancelar pedido\".");
        }

        if (await periods.CheckAsync(adjustment.Day, cancellationToken) is { } closed)
        {
            return closed;
        }

        db.Entry(adjustment).Property(a => a.Version).OriginalValue = input.Version;
        adjustment.Status = TimeAdjustmentStatus.Rejected;
        adjustment.DecidedBy = currentUser.UserId;
        adjustment.DecidedAt = clock.GetUtcNow();
        adjustment.DecisionComment = comment;
        if (adjustment.RequestedBy is { } requester)
        {
            notifications.Notify(requester, DecidedType, Cut(TimeAdjustmentRules.RejectedNotice(adjustment.Day, comment), 200), RequesterLink(adjustment));
        }

        await db.SaveChangesAsync(cancellationToken);
        return await ViewAsync(id, cancellationToken);
    }

    /// <summary>"Cancelar pedido" (CA 5): só quem pediu, enquanto pendente; o motivo é opcional.</summary>
    public async Task<Result<TimeAdjustmentView>> CancelAsync(Guid id, TimeAdjustmentDecisionInput input, Guid? employeeId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var comment = input.Comment?.Trim();
        if (comment is { Length: > TimeAdjustmentRules.CommentMax })
        {
            return Invalid("comment", $"Escreva o motivo com até {TimeAdjustmentRules.CommentMax} caracteres.");
        }

        var pending = await PendingAsync(id, input.Version, cancellationToken, employeeId);
        if (pending.IsFailure)
        {
            return pending.Error!;
        }

        var adjustment = pending.Value;
        if (currentUser.UserId is not { } me || adjustment.RequestedBy != me)
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Só quem fez o pedido pode cancelar.");
        }

        db.Entry(adjustment).Property(a => a.Version).OriginalValue = input.Version;
        adjustment.Status = TimeAdjustmentStatus.Cancelled;
        adjustment.DecidedBy = me;
        adjustment.DecidedAt = clock.GetUtcNow();
        adjustment.DecisionComment = string.IsNullOrEmpty(comment) ? null : comment;
        await db.SaveChangesAsync(cancellationToken);
        return await ViewAsync(id, cancellationToken, employeeId);
    }

    /// <summary>
    /// "Aprovar selecionados" (CA 15, R-PRP-6): cada pedido na sua transação; os que falham (próprio, já decidido, período fechado,
    /// conflito) voltam com o motivo e os outros ficam aprovados.
    /// </summary>
    public async Task<Result<TimeAdjustmentBatchResult>> ApproveManyAsync(TimeAdjustmentBatchInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Items is not { Count: > 0 } items || items.Count > 100 || items.Select(i => i.Id).Distinct().Count() != items.Count)
        {
            return Invalid("items", "Selecione de 1 a 100 pedidos, sem repetir.");
        }

        var results = new List<TimeAdjustmentBatchResultItem>(items.Count);
        foreach (var item in items)
        {
            db.ChangeTracker.Clear();
            try
            {
                var approved = await ApproveAsync(item.Id, new TimeAdjustmentDecisionInput(item.Version), cancellationToken);
                results.Add(approved.IsSuccess
                    ? new TimeAdjustmentBatchResultItem(item.Id, true, null, null)
                    : new TimeAdjustmentBatchResultItem(item.Id, false, approved.Error!.Code, approved.Error.Message));
            }
            catch (DbUpdateConcurrencyException)
            {
                results.Add(new TimeAdjustmentBatchResultItem(item.Id, false, "concurrency_conflict", "O ajuste mudou desde que você abriu. Recarregue."));
            }
        }

        db.ChangeTracker.Clear();
        return new TimeAdjustmentBatchResult(results.Count(r => r.Approved), results.Count(r => !r.Approved), results);
    }

    /// <summary>
    /// Documento do abono pelo RH (CA 8), com <c>hr.view_health</c>: o pedido precisa ser do funcionário da rota e ser um abono; ClamAV e storage
    /// com o prefixo do tenant.
    /// </summary>
    public async Task<Result<AttachmentDto>> AttachAsync(Guid employeeId, Guid id, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        if (!await permissions.HasPermissionAsync("hr", HealthAction, cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Só quem tem a permissão Ver dados de saúde anexa o documento do abono.");
        }

        return await AttachCheckedAsync(employeeId, id, fileName, content, cancellationToken);
    }

    /// <summary>O próprio funcionário anexa o documento ao abono dele (CA 8), sem <c>hr.view_health</c>.</summary>
    public Task<Result<AttachmentDto>> AttachMineAsync(EmployeeSummary employee, Guid id, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employee);
        return AttachCheckedAsync(employee.Id, id, fileName, content, cancellationToken);
    }

    /// <summary>Download pelo RH (CA 8): <c>hr.view_health</c> na rota, o anexo precisa ser do pedido e o pedido do funcionário; grava <c>SensitiveRead</c>.</summary>
    public async Task<Result<AttachmentContent>> DocumentAsync(Guid employeeId, Guid id, Guid attachmentId, CancellationToken cancellationToken = default)
    {
        if (!await db.TimeAdjustments.AnyAsync(a => a.Id == id && a.EmployeeId == employeeId && a.Kind == TimeAdjustmentKind.Absence, cancellationToken))
        {
            return NotFound();
        }

        return await attachments.DownloadCheckedAsync(DocumentOwner, id, attachmentId, cancellationToken);
    }

    /// <summary>O funcionário baixa só o que ele mesmo enviou ao próprio pedido (CA 8).</summary>
    public async Task<Result<AttachmentContent>> MyDocumentAsync(EmployeeSummary employee, Guid id, Guid attachmentId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employee);
        var me = currentUser.UserId;
        var mine = (await attachments.ListCheckedAsync(DocumentOwner, id, cancellationToken)).Any(a => a.Id == attachmentId)
            && me is { } user && (await attachments.FindAsync(attachmentId, cancellationToken))?.UploadedBy == user;
        if (!mine || !await db.TimeAdjustments.AnyAsync(a => a.Id == id && a.EmployeeId == employee.Id, cancellationToken))
        {
            return Error.NotFound(Error.Codes.NotFound, "Anexo não encontrado.");
        }

        return await attachments.DownloadCheckedAsync(DocumentOwner, id, attachmentId, cancellationToken);
    }

    private async Task<Result<AttachmentDto>> AttachCheckedAsync(Guid employeeId, Guid id, string fileName, Stream content, CancellationToken ct)
    {
        var adjustment = await db.TimeAdjustments.AsNoTracking().Where(a => a.Id == id && a.EmployeeId == employeeId).Select(a => new { a.Kind, a.Status, a.Day }).FirstOrDefaultAsync(ct);
        if (adjustment is null)
        {
            return NotFound();
        }

        if (adjustment.Kind != TimeAdjustmentKind.Absence)
        {
            return Error.Validation("time_adjustment_no_document", "Só o abono de ausência recebe documento.");
        }

        if (adjustment.Status != TimeAdjustmentStatus.Pending)
        {
            return Error.Conflict(DecidedCode, "O pedido já foi decidido e não recebe mais documento.");
        }

        if (await periods.CheckAsync(adjustment.Day, ct) is { } closed)
        {
            return closed;
        }

        return await attachments.UploadCheckedAsync(DocumentOwner, id, fileName, content, StoragePrefix(), ct);
    }

    private async Task<Result<TimeAdjustment>> PendingAsync(Guid id, uint version, CancellationToken ct, Guid? employeeId = null)
    {
        var adjustment = await db.TimeAdjustments.FirstOrDefaultAsync(a => a.Id == id && (employeeId == null || a.EmployeeId == employeeId), ct);
        if (adjustment is null)
        {
            return NotFound();
        }

        if (adjustment.Status != TimeAdjustmentStatus.Pending)
        {
            return Error.Conflict(DecidedCode, "O ajuste já foi decidido.");
        }

        return adjustment.Version != version ? Error.Conflict("concurrency_conflict", "O ajuste mudou desde que você abriu. Recarregue.") : adjustment;
    }

    /// <summary>Envolvido (CA 13): quem pediu ou o usuário ligado ao funcionário dono das marcações.</summary>
    private async Task<bool> InvolvedAsync(TimeAdjustment adjustment, Guid actor, CancellationToken ct) =>
        adjustment.RequestedBy == actor || await links.EmployeeOfUserAsync(actor, ct) == adjustment.EmployeeId;

    private async Task<Result<TimeAdjustmentView>> ViewAsync(Guid id, CancellationToken ct, Guid? selfEmployeeId = null)
    {
        var views = await ViewsAsync(db.TimeAdjustments.AsNoTracking().Where(a => a.Id == id), selfEmployeeId, ct);
        return views.Count == 0 ? NotFound() : views[0];
    }

    private static string RequesterLink(TimeAdjustment adjustment) =>
        adjustment.Source == TimeAdjustmentSource.Employee ? MineLink : "/app/rh/funcionarios/" + adjustment.EmployeeId + "?aba=ponto";

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
