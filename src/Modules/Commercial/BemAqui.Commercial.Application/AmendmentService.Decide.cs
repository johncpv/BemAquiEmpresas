using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

public sealed partial class AmendmentService
{
    public const string NotificationType = "commercial.amendment";

    /// <summary>Enviar para aprovação (CA 5): avisa quem aprova em contratos e projetos.</summary>
    public async Task<Result<AmendmentDetail>> SubmitAsync(Guid contractId, Guid id, uint version, CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(contractId, id, version, AmendmentStatus.Draft, "Só o aditivo em rascunho é enviado.", cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var (amendment, contract) = loaded.Value;
        amendment.Status = AmendmentStatus.PendingApproval;
        amendment.SubmittedAt = clock.GetUtcNow();
        foreach (var user in await users.UsersWithPermissionAsync("projects.approve", cancellationToken))
        {
            notifications.Notify(user, NotificationType, $"Aditivo {amendment.Number} do {contract.Number} aguarda aprovação.", Link(amendment));
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(contractId, id, cancellationToken);
    }

    /// <summary>
    /// Aprovar (CA 6, 9): quem registrou não aprova, com as exceções da OP; na transação, soma no orçado e na data prometida e
    /// publica <see cref="ContractAmended"/>. Aprovação dupla: só uma grava (xmin).
    /// </summary>
    public async Task<Result<AmendmentDetail>> ApproveAsync(Guid contractId, Guid id, uint version, CancellationToken cancellationToken = default)
    {
        var loaded = await LoadAsync(contractId, id, version, AmendmentStatus.PendingApproval, "Só o aditivo enviado é aprovado.", cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var (amendment, contract) = loaded.Value;
        var rule = await DecisionRuleAsync(amendment, cancellationToken);
        if (!rule.Allowed)
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Quem registrou o aditivo não pode aprová-lo.");
        }

        var project = await db.Projects.FirstOrDefaultAsync(p => p.ContractId == contractId, cancellationToken);
        amendment.Status = AmendmentStatus.Approved;
        amendment.ApprovedAt = clock.GetUtcNow();
        amendment.ApprovedBy = currentUser.UserId;
        amendment.SelfApproved = rule.Self;
        if (project is not null)
        {
            var budget = await db.ProjectBudgets.Where(b => b.ProjectId == project.Id).ToListAsync(cancellationToken);
            foreach (var (type, delta) in Deltas(amendment.BudgetDeltas))
            {
                if (budget.FirstOrDefault(b => b.CostType == type) is { } line)
                {
                    line.BudgetedAmount += delta;
                }
                else
                {
                    db.ProjectBudgets.Add(new ProjectBudget { ProjectId = project.Id, CostType = type, BudgetedAmount = delta });
                }
            }

            project.PromisedDate = project.PromisedDate?.AddDays(amendment.DaysDelta);
        }

        var milestones = await db.ContractAmendmentMilestones.AsNoTracking().Where(m => m.AmendmentId == id).OrderBy(m => m.Sequence).ToListAsync(cancellationToken);
        var reductions = await db.ContractAmendmentReductions.AsNoTracking().Where(r => r.AmendmentId == id).ToListAsync(cancellationToken);
        events.Publish(new ContractAmended(
            contractId, project?.Id ?? Guid.Empty, amendment.Id, amendment.Number, contract.PartyId, contract.RevenueAccountId, BusinessTime.Today(clock), amendment.ValueDelta,
            milestones.Select(m => new ContractMilestoneInfo(m.Id, m.Sequence, $"Aditivo {amendment.Number} — {m.Description}", m.Amount, m.Trigger, m.TriggerEvent, m.DueDate, m.DaysAfterEvent, m.Percent)).ToList(),
            reductions.Select(r => new AmendmentReduction(r.ReceivableId, r.NewAmount)).ToList()));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("concurrency_conflict", "O aditivo já foi decidido por outra pessoa. Recarregue.");
        }

        return await GetAsync(contractId, id, cancellationToken);
    }

    /// <summary>Reprovar (CA 8): comentário obrigatório; o autor é avisado.</summary>
    public async Task<Result<AmendmentDetail>> RejectAsync(Guid contractId, Guid id, AmendmentCommentInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await LoadAsync(contractId, id, input.Version, AmendmentStatus.PendingApproval, "Só o aditivo enviado é reprovado.", cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var comment = input.Comment?.Trim();
        if (string.IsNullOrEmpty(comment) || comment.Length > 500)
        {
            return QuoteService.Invalid("comment", "Diga o motivo da reprovação, com até 500 caracteres.");
        }

        var (amendment, contract) = loaded.Value;
        amendment.Status = AmendmentStatus.Rejected;
        amendment.RejectionComment = comment;
        if (amendment.CreatedBy is { } author)
        {
            notifications.Notify(author, NotificationType, $"Aditivo {amendment.Number} do {contract.Number} reprovado: {comment}", Link(amendment));
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(contractId, id, cancellationToken);
    }

    /// <summary>Regra do autor (D6, como a OP): só o Proprietário de um membro, ou com o parâmetro ligado, aprova o próprio.</summary>
    private async Task<(bool Allowed, bool Self)> DecisionRuleAsync(ContractAmendment amendment, CancellationToken ct)
    {
        if (amendment.CreatedBy is not { } author || author != currentUser.UserId)
        {
            return (true, false);
        }

        var owners = await users.OwnerIdsAsync(ct);
        var allowed = owners.Contains(author) && (owners.Count == 1 || (await financeParameters.LimitsAsync(ct)).OwnerCanApproveOwn);
        return (allowed, allowed);
    }

    private async Task<Result<(ContractAmendment Amendment, Contract Contract)>> LoadAsync(Guid contractId, Guid id, uint version, AmendmentStatus expected, string message, CancellationToken ct)
    {
        var amendment = await db.ContractAmendments.FirstOrDefaultAsync(a => a.Id == id && a.ContractId == contractId, ct);
        if (amendment is null)
        {
            return NotFound();
        }

        if (amendment.Status != expected)
        {
            return Error.Conflict("amendment_status", message);
        }

        if (amendment.Version != version)
        {
            return Stale();
        }

        var contract = await db.Contracts.FirstAsync(c => c.Id == contractId, ct);
        if (contract.Status != ContractStatus.Active)
        {
            return Error.Conflict("contract_not_active", "O contrato não está ativo.");
        }

        db.Entry(amendment).Property(a => a.Version).OriginalValue = version;
        return (amendment, contract);
    }

    private static string Link(ContractAmendment a) => $"/app/contratos/{a.ContractId}/aditivos/{a.Id}";
}
