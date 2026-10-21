using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

public sealed partial class AmendmentService
{
    /// <summary>Novo aditivo em rascunho (CA 1): número sequencial no contrato.</summary>
    public async Task<Result<AmendmentDetail>> CreateAsync(Guid contractId, AmendmentInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var contract = await db.Contracts.FirstOrDefaultAsync(c => c.Id == contractId, cancellationToken);
        if (contract is null)
        {
            return ContractService.NotFound();
        }

        if (contract.Status != ContractStatus.Active)
        {
            return Error.Conflict("contract_not_active", "Só o contrato ativo recebe aditivo.");
        }

        var last = await db.ContractAmendments.Where(a => a.ContractId == contractId).Select(a => (int?)a.Number).MaxAsync(cancellationToken);
        var amendment = new ContractAmendment { ContractId = contractId, Number = (last ?? 0) + 1, CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId };
        var validated = await ValidateAsync(contract, amendment, input, cancellationToken);
        if (validated.IsFailure)
        {
            return validated.Error!;
        }

        db.ContractAmendments.Add(amendment);
        Apply(amendment, input, validated.Value);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(contractId, amendment.Id, cancellationToken);
    }

    /// <summary>Edita o rascunho (CA 2 a 4).</summary>
    public async Task<Result<AmendmentDetail>> UpdateAsync(Guid contractId, Guid id, AmendmentInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var amendment = await db.ContractAmendments.FirstOrDefaultAsync(a => a.Id == id && a.ContractId == contractId, cancellationToken);
        if (amendment is null)
        {
            return NotFound();
        }

        if (amendment.Status != AmendmentStatus.Draft)
        {
            return Error.Conflict("amendment_not_draft", "Só o aditivo em rascunho é editado.");
        }

        if (input.Version is not { } version || amendment.Version != version)
        {
            return Stale();
        }

        var contract = await db.Contracts.FirstAsync(c => c.Id == contractId, cancellationToken);
        var validated = await ValidateAsync(contract, amendment, input, cancellationToken);
        if (validated.IsFailure)
        {
            return validated.Error!;
        }

        db.Entry(amendment).Property(a => a.Version).OriginalValue = version;
        db.ContractAmendmentMilestones.RemoveRange(await db.ContractAmendmentMilestones.Where(m => m.AmendmentId == id).ToListAsync(cancellationToken));
        db.ContractAmendmentReductions.RemoveRange(await db.ContractAmendmentReductions.Where(r => r.AmendmentId == id).ToListAsync(cancellationToken));
        Apply(amendment, input, validated.Value);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(contractId, id, cancellationToken);
    }

    private void Apply(ContractAmendment amendment, AmendmentInput input, Validated v)
    {
        amendment.Description = v.Description;
        amendment.ValueDelta = input.ValueDelta;
        amendment.DaysDelta = input.DaysDelta;
        amendment.BudgetDeltas = Serialize(v.Budget);
        db.ContractAmendmentMilestones.AddRange(v.Milestones.Select(m => new ContractAmendmentMilestone
        {
            AmendmentId = amendment.Id, Sequence = m.Sequence, Description = m.Description, Percent = m.Percent, Amount = m.Amount, Trigger = m.Trigger, TriggerEvent = m.TriggerEvent,
            DueDate = m.DueDate, DaysAfterEvent = m.DaysAfterEvent,
        }));
        db.ContractAmendmentReductions.AddRange(v.Reductions.Select(r => new ContractAmendmentReduction { AmendmentId = amendment.Id, ReceivableId = r.ReceivableId, NewAmount = r.NewAmount }));
    }
}
