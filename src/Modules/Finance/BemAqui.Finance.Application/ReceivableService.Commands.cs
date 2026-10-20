using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

public sealed partial class ReceivableService
{
    private const decimal MaxAmount = 9_999_999_999_999_999.99m;

    /// <summary>Título manual (CA 1, 2 e 4): número CR na mesma transação.</summary>
    public async Task<Result<ReceivablesCreated>> CreateAsync(ReceivableInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await ValidateAsync(input.PartyId, input.Description, input.AccountId, input.CostCenterId, input.ProjectId, input.Amount, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        if (input.DueDate < input.IssueDate)
        {
            return Invalid("dueDate", "O vencimento não pode ser antes da emissão.");
        }

        return await SaveNewAsync([(input.Description.Trim(), input.Amount, input.DueDate)], input.PartyId, input.AccountId, input.CostCenterId, input.ProjectId, input.IssueDate, null, cancellationToken);
    }

    /// <summary>Parcelas pela condição (CA 3): a mesma divisão do <see cref="IPaymentTerms.SplitAsync"/>, números seguidos.</summary>
    public async Task<Result<ReceivablesCreated>> CreateInstallmentsAsync(ReceivableInstallmentsInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await ValidateAsync(input.PartyId, input.Description, input.AccountId, input.CostCenterId, input.ProjectId, input.Amount, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var split = await terms.SplitAsync(input.PaymentTermId, input.Amount, input.BaseDate, cancellationToken);
        if (split is null || split.Count == 0)
        {
            return Invalid("paymentTermId", "Escolha uma condição de pagamento da lista.");
        }

        if (split.Any(p => p.DueDate < input.IssueDate))
        {
            return Invalid("baseDate", "O vencimento não pode ser antes da emissão.");
        }

        var description = input.Description.Trim();
        var parcels = split.Count == 1
            ? [(description, split[0].Amount, split[0].DueDate)]
            : split.Select(p => ($"{description} ({p.Number}/{split.Count})", p.Amount, p.DueDate)).ToList();
        return await SaveNewAsync(parcels, input.PartyId, input.AccountId, input.CostCenterId, input.ProjectId, input.IssueDate, split.Count > 1 ? Guid.CreateVersion7() : null, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<InstallmentDue>>> SplitPreviewAsync(SplitPreviewInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Amount is <= 0 or > MaxAmount)
        {
            return Invalid("amount", "Informe um valor maior que zero.");
        }

        var split = await terms.SplitAsync(input.PaymentTermId, input.Amount, input.BaseDate, cancellationToken);
        return split is null ? Invalid("paymentTermId", "Escolha uma condição de pagamento da lista.") : Result<IReadOnlyList<InstallmentDue>>.Success(split);
    }

    /// <summary>Dados editáveis (CA 19): valor só em título manual sem baixa; o do contrato muda pelo contrato ou aditivo.</summary>
    public async Task<Result<ReceivableDetail>> UpdateAsync(Guid id, ReceivableUpdateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var receivable = await db.Receivables.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (receivable is null)
        {
            return NotFound();
        }

        if (Stale(receivable, input.Version) is { } stale)
        {
            return stale;
        }

        if (receivable.Status == ReceivableStatus.Cancelled)
        {
            return Error.Conflict("receivable_cancelled", "Título cancelado não muda.");
        }

        if (await ValidateAsync(receivable.PartyId, input.Description, input.AccountId, input.CostCenterId, input.ProjectId, input.Amount, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        if (input.Amount != receivable.Amount)
        {
            if (receivable.Origin == DocumentOrigin.Contract)
            {
                return ContractProtected(receivable);
            }

            if (receivable.PaidAmount > 0)
            {
                return Error.Conflict("receivable_has_settlements", "O título já tem baixa: o valor não muda. Estorne as baixas antes.");
            }
        }

        Expect(receivable, input.Version);
        receivable.Description = input.Description.Trim();
        receivable.AccountId = input.AccountId;
        receivable.CostCenterId = input.CostCenterId;
        receivable.ProjectId = input.ProjectId;
        receivable.Amount = input.Amount;
        await db.SaveChangesAsync(cancellationToken);
        return (await DetailsAsync([receivable], cancellationToken))[0];
    }

    /// <summary>Vencimento novo com motivo (D14): só título em aberto sem baixa; o motivo vai para a notificação.</summary>
    public async Task<Result<ReceivableDetail>> ChangeDueDateAsync(Guid id, DueDateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var receivable = await db.Receivables.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (receivable is null)
        {
            return NotFound();
        }

        if (Stale(receivable, input.Version) is { } stale)
        {
            return stale;
        }

        if (receivable.Status != ReceivableStatus.Open)
        {
            return Error.Conflict("receivable_not_open", "Só o vencimento de título em aberto, sem baixa, muda.");
        }

        if (ReasonError(input.Reason) is { } missing)
        {
            return missing;
        }

        if (input.DueDate < receivable.IssueDate)
        {
            return Invalid("dueDate", "O vencimento não pode ser antes da emissão.");
        }

        Expect(receivable, input.Version);
        var old = receivable.DueDate;
        receivable.DueDate = input.DueDate;
        await NotifyOwnersAsync($"Vencimento de {receivable.Number} alterado de {old:dd/MM/yyyy} para {input.DueDate:dd/MM/yyyy} por {await WhoAsync(cancellationToken)}: {input.Reason.Trim()}", receivable.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return (await DetailsAsync([receivable], cancellationToken))[0];
    }

    /// <summary>Cancelamento com motivo (CA 17, 18 e 20): sem baixa ativa; acima do limite, só com <c>finance.approve</c>.</summary>
    public async Task<Result<ReceivableDetail>> CancelAsync(Guid id, ReasonInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var receivable = await db.Receivables.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (receivable is null)
        {
            return NotFound();
        }

        if (Stale(receivable, input.Version) is { } stale)
        {
            return stale;
        }

        if (receivable.Status == ReceivableStatus.Cancelled)
        {
            return Error.Conflict("receivable_cancelled", "O título já está cancelado.");
        }

        if (ReasonError(input.Reason) is { } missing)
        {
            return missing;
        }

        var originCancelled = false;
        if (receivable.Origin == DocumentOrigin.Contract)
        {
            originCancelled = receivable.OriginId is { } contractId && await IsContractCancelledAsync(contractId, cancellationToken);
            if (!originCancelled)
            {
                return ContractProtected(receivable);
            }
        }

        if (await db.ReceivableSettlements.AnyAsync(s => s.ReceivableId == id && s.ReversedAt == null, cancellationToken))
        {
            return Error.Conflict("receivable_has_settlements", "Estorne as baixas antes de cancelar.");
        }

        var limits = await parameters.LimitsAsync(cancellationToken);
        // Título de implantação, com a implantação aberta, cancela sem o limite (FIN-00 CA 8).
        var openingFree = receivable.Origin == DocumentOrigin.Opening && !await db.OpeningSetups.AnyAsync(s => s.CompletedAt != null, cancellationToken);
        if (!openingFree && receivable.Balance > limits.CancelApprovalAmount && !await permissions.HasPermissionAsync("finance", "approve", cancellationToken))
        {
            return Error.Forbidden("cancel_requires_approval", $"Saldo acima de {Money(limits.CancelApprovalAmount)}: só quem aprova em Caixa e pagamentos pode cancelar.");
        }

        Expect(receivable, input.Version);
        var reason = input.Reason.Trim();
        var balance = receivable.Balance;
        receivable.Status = ReceivableStatus.Cancelled;
        receivable.CancelReason = originCancelled ? "Origem cancelada: " + reason : reason;
        receivable.CancelledAt = clock.GetUtcNow();
        receivable.CancelledBy = currentUser.UserId;
        await NotifyOwnersAsync($"Título {receivable.Number} de {Money(balance)} cancelado por {await WhoAsync(cancellationToken)}: {reason}", receivable.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return (await DetailsAsync([receivable], cancellationToken))[0];
    }

    private async Task<bool> IsContractCancelledAsync(Guid contractId, CancellationToken ct)
    {
        foreach (var source in contracts)
        {
            if (await source.IsCancelledAsync(contractId, ct) == true)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<Result<ReceivablesCreated>> SaveNewAsync(IReadOnlyList<(string Description, decimal Amount, DateOnly DueDate)> parcels, Guid partyId, Guid accountId, Guid? costCenterId, Guid? projectId, DateOnly issueDate, Guid? groupId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var created = new List<Receivable>();
        foreach (var (description, amount, dueDate) in parcels)
        {
            var receivable = new Receivable
            {
                Number = await numbers.NextAsync(DocType, ct), PartyId = partyId, Description = description, AccountId = accountId, CostCenterId = costCenterId, ProjectId = projectId,
                Amount = amount, IssueDate = issueDate, DueDate = dueDate, Status = ReceivableStatus.Open, Origin = DocumentOrigin.Manual, InstallmentGroupId = groupId,
                CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
            };
            db.Receivables.Add(receivable);
            created.Add(receivable);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new ReceivablesCreated(await DetailsAsync(created, ct));
    }

    private async Task<Error?> ValidateAsync(Guid partyId, string? description, Guid accountId, Guid? costCenterId, Guid? projectId, decimal amount, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > 200)
        {
            return Invalid("description", "Informe a descrição, com até 200 caracteres.");
        }

        if (amount is <= 0 or > MaxAmount)
        {
            return Invalid("amount", "Informe um valor maior que zero.");
        }

        var party = await parties.FindAsync(partyId, ct);
        if (party is null || party.IsDeleted || !party.IsActive || !party.Roles.Contains(PartyRoleKind.Customer))
        {
            return Invalid("partyId", "Escolha um terceiro com o papel Cliente.");
        }

        var found = await chart.FindAsync([accountId], ct);
        var account = found.Count > 0 ? found[0] : null;
        if (account is null || !account.IsActive || account.Type != AccountType.Revenue)
        {
            return Invalid("accountId", "Escolha uma conta de receita ativa.");
        }

        if (!account.AcceptsEntries)
        {
            return Invalid("accountId", "Escolha uma conta analítica.");
        }

        if (costCenterId is { } center && !await db.CostCenters.AnyAsync(c => c.Id == center && c.IsActive, ct))
        {
            return Invalid("costCenterId", "Escolha um centro de custo ativo.");
        }

        // O projeto entra com a aprovação do orçamento (COM-04a); antes disso o campo nem aparece.
        return projectId is null ? null : Invalid("projectId", "Os projetos entram com a aprovação do orçamento.");
    }

    private static Error ContractProtected(Receivable receivable) =>
        Error.Conflict("receivable_contract", $"Título do contrato {receivable.Number}: ajuste pelo contrato ou pelo aditivo.");

    private static Error? ReasonError(string? reason) =>
        string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 200 ? Invalid("reason", "Informe o motivo, com até 200 caracteres.") : null;
}
