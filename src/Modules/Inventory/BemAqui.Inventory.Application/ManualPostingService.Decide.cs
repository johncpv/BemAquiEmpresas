using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

public sealed partial class ManualPostingService
{
    /// <summary>
    /// "Aprovar lançamento" (CA 10, 11): na mesma transação o lançamento fica <c>Posted</c> e os movimentos nascem pelo motor (a saída
    /// pelo custo médio do momento da aprovação). O aprovador decide sobre o que foi enviado: campo a mais no corpo é 422. Quem lançou
    /// não aprova, salvo o Proprietário único ("Autoaprovado" e aviso ao Proprietário).
    /// </summary>
    public async Task<Result<ManualPostingDetail>> ApproveAsync(Guid id, ManualDecisionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var checkedInput = await DecisionGuardAsync(input, requireComment: false, cancellationToken);
        if (checkedInput.IsFailure)
        {
            return checkedInput.Error!;
        }

        var posting = await PendingAsync(id, input.Version, cancellationToken);
        if (posting.IsFailure)
        {
            return posting.Error!;
        }

        var me = currentUser.UserId;
        var self = false;
        if (me is { } actor && posting.Value.RequestedBy == actor)
        {
            if (!await IsDecidingAloneAsync(actor, cancellationToken))
            {
                return Error.Forbidden("own_manual_posting", "Quem lança não aprova o próprio lançamento.");
            }

            self = true;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var entity = posting.Value;
        db.Entry(entity).Property(p => p.Version).OriginalValue = input.Version;
        entity.DecidedBy = me;
        entity.DecidedAt = clock.GetUtcNow();
        entity.DecisionComment = checkedInput.Value;
        entity.SelfApproved = self;
        // Grava a decisão antes do motor: duas aprovações simultâneas param no xmin e só uma segue (CA 10).
        entity.Status = ManualPostingStatus.Posted;
        await db.SaveChangesAsync(cancellationToken);
        var lines = await db.ManualPostingLines.Where(l => l.PostingId == id).OrderBy(l => l.Sequence).ToListAsync(cancellationToken);
        var posted = await PostLinesAsync(entity, lines, cancellationToken);
        if (posted.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            return posted.Error!;
        }

        if (self)
        {
            await NotifySelfApprovedAsync($"aprovou o próprio lançamento {entity.Number}", Link + entity.Id, cancellationToken);
        }
        else if (entity.RequestedBy is { } author)
        {
            notifications.Notify(author, DecisionType, $"Lançamento {entity.Number} aprovado.", Link + entity.Id);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(id, cancellationToken);
    }

    /// <summary>"Reprovar lançamento" (CA 10): comentário obrigatório; fica <c>Rejected</c> sem movimento e o autor é avisado.</summary>
    public async Task<Result<ManualPostingDetail>> RejectAsync(Guid id, ManualDecisionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var comment = await DecisionGuardAsync(input, requireComment: true, cancellationToken);
        if (comment.IsFailure)
        {
            return comment.Error!;
        }

        var posting = await PendingAsync(id, input.Version, cancellationToken);
        if (posting.IsFailure)
        {
            return posting.Error!;
        }

        var entity = posting.Value;
        if (currentUser.UserId is { } me && entity.RequestedBy == me)
        {
            return Error.Forbidden("own_manual_posting", "Para desistir do próprio lançamento, use \"Cancelar lançamento\".");
        }

        db.Entry(entity).Property(p => p.Version).OriginalValue = input.Version;
        entity.Status = ManualPostingStatus.Rejected;
        entity.DecidedBy = currentUser.UserId;
        entity.DecidedAt = clock.GetUtcNow();
        entity.DecisionComment = comment.Value;
        if (entity.RequestedBy is { } author)
        {
            notifications.Notify(author, DecisionType, $"Lançamento {entity.Number} reprovado: {Cut(comment.Value!, 120)}", Link + entity.Id);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(id, cancellationToken);
    }

    /// <summary>"Cancelar lançamento" (CA 10): só o autor, enquanto pendente.</summary>
    public async Task<Result<ManualPostingDetail>> CancelAsync(Guid id, ManualCancelInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var posting = await PendingAsync(id, input.Version, cancellationToken);
        if (posting.IsFailure)
        {
            return posting.Error!;
        }

        var entity = posting.Value;
        if (currentUser.UserId is not { } me || entity.RequestedBy != me)
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Só quem lançou cancela o lançamento.");
        }

        db.Entry(entity).Property(p => p.Version).OriginalValue = input.Version;
        entity.Status = ManualPostingStatus.Cancelled;
        entity.DecidedBy = me;
        entity.DecidedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(id, cancellationToken);
    }

    /// <summary>Só a decisão e o comentário (CA 10); aprovar e reprovar exigem <c>inventory.approve</c>.</summary>
    private async Task<Result<string?>> DecisionGuardAsync(ManualDecisionInput input, bool requireComment, CancellationToken ct)
    {
        if (input.Extra is { Count: > 0 } extra)
        {
            const string message = "Quem aprova decide sobre o que foi enviado: envie só a decisão e o comentário.";
            return Error.Validation("decision_only", message, extra.Keys.ToDictionary(k => k, _ => new[] { message }));
        }

        if (!await permissions.HasPermissionAsync("inventory", "approve", ct))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Quem decide o lançamento é quem aprova no Estoque.");
        }

        var comment = Clean(input.Comment);
        if (requireComment && comment is null)
        {
            return Invalid("comment", "Escreva o motivo da reprovação.");
        }

        return comment is { Length: > MaxComment } ? Invalid("comment", $"Escreva o comentário com até {MaxComment} caracteres.") : comment;
    }

    private async Task<Result<ManualPosting>> PendingAsync(Guid id, uint version, CancellationToken ct)
    {
        var posting = await db.ManualPostings.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (posting is null)
        {
            return NotFound();
        }

        if (posting.Status != ManualPostingStatus.Pending)
        {
            return Error.Conflict("manual_posting_decided", "O lançamento já foi decidido.");
        }

        return posting.Version != version ? Error.Conflict("concurrency_conflict", "O lançamento mudou desde que você abriu. Recarregue.") : posting;
    }

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
