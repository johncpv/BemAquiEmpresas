using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

public sealed partial class PayableService
{
    private const decimal MaxAmount = 9_999_999_999_999_999.99m;
    private static readonly DocumentOrigin[] EditableOrigins = [DocumentOrigin.Manual, DocumentOrigin.Recurring, DocumentOrigin.Opening];

    private sealed record Retentions(decimal Iss, decimal Inss, decimal Ir, decimal Pcc)
    {
        public decimal Sum => Iss + Inss + Ir + Pcc;
    }

    /// <summary>Título manual (CA 1, 2, 4 a 6): número CP na mesma transação; avisos da forma de pagamento na resposta.</summary>
    public async Task<Result<PayablesCreated>> CreateAsync(PayableInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var retentions = new Retentions(input.RetentionIss, input.RetentionInss, input.RetentionIr, input.RetentionPcc);
        if (await ValidateAsync(input.PartyId, input.Description, input.AccountId, input.CostCenterId, input.Amount, retentions, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        if (input.DueDate < input.IssueDate)
        {
            return ReceivableService.Invalid("dueDate", "O vencimento não pode ser antes da emissão.");
        }

        var (details, badDetails) = DetailsOf(input.PaymentDetails);
        if (badDetails is not null)
        {
            return badDetails;
        }

        return await SaveNewAsync([(input.Description.Trim(), input.Amount, input.DueDate, retentions)], input, details, null, cancellationToken);
    }

    /// <summary>Parcelas pela condição (CA 3): as retenções ficam na primeira parcela, que é a da nota.</summary>
    public async Task<Result<PayablesCreated>> CreateInstallmentsAsync(PayableInstallmentsInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var retentions = new Retentions(input.RetentionIss, input.RetentionInss, input.RetentionIr, input.RetentionPcc);
        if (await ValidateAsync(input.PartyId, input.Description, input.AccountId, input.CostCenterId, input.Amount, retentions, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var split = await terms.SplitAsync(input.PaymentTermId, input.Amount, input.BaseDate, cancellationToken);
        if (split is null || split.Count == 0)
        {
            return ReceivableService.Invalid("paymentTermId", "Escolha uma condição de pagamento da lista.");
        }

        if (split[0].Amount <= retentions.Sum)
        {
            return ReceivableService.Invalid("retentionIss", "As retenções passam do valor da primeira parcela.");
        }

        var (details, badDetails) = DetailsOf(input.PaymentDetails);
        if (badDetails is not null)
        {
            return badDetails;
        }

        var description = input.Description.Trim();
        var none = new Retentions(0, 0, 0, 0);
        var parcels = split.Select(p => (split.Count == 1 ? description : $"{description} ({p.Number}/{split.Count})", p.Amount, p.DueDate, p.Number == 1 ? retentions : none)).ToList();
        var common = new PayableInput(input.PartyId, description, input.AccountId, input.CostCenterId, input.Amount, input.IssueDate, input.BaseDate, 0, 0, 0, 0, null);
        return await SaveNewAsync(parcels, common, details, split.Count > 1 ? Guid.CreateVersion7() : null, cancellationToken);
    }

    /// <summary>Dados (CA 11 e 13): em OP só descrição e vencimento; título gerado por outra origem não troca valor.</summary>
    public async Task<Result<PayableDetail>> UpdateAsync(Guid id, PayableUpdateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var payable = await db.Payables.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (payable is null)
        {
            return NotFound();
        }

        if (Stale(payable, input.Version) is { } stale)
        {
            return stale;
        }

        if (payable.Status is PayableStatus.Cancelled or PayableStatus.Paid)
        {
            return Error.Conflict("payable_closed", "Título pago ou cancelado não muda.");
        }

        var retentions = new Retentions(input.RetentionIss, input.RetentionInss, input.RetentionIr, input.RetentionPcc);
        var financial = input.Amount != payable.Amount || input.AccountId != payable.AccountId || input.CostCenterId != payable.CostCenterId || retentions.Sum != payable.Retentions
            || input.RetentionIss != payable.RetentionIss || input.RetentionInss != payable.RetentionInss || input.RetentionIr != payable.RetentionIr || input.RetentionPcc != payable.RetentionPcc;
        if (payable.InPaymentOrder && financial)
        {
            return InOrder(payable);
        }

        if (!EditableOrigins.Contains(payable.Origin) && input.Amount != payable.Amount)
        {
            return OriginProtected(payable);
        }

        if (await ValidateAsync(payable.PartyId, input.Description, input.AccountId, input.CostCenterId, input.Amount, retentions, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        if (input.DueDate < payable.IssueDate)
        {
            return ReceivableService.Invalid("dueDate", "O vencimento não pode ser antes da emissão.");
        }

        Expect(payable, input.Version);
        payable.Description = input.Description.Trim();
        payable.AccountId = input.AccountId;
        payable.CostCenterId = input.CostCenterId;
        payable.Amount = input.Amount;
        (payable.RetentionIss, payable.RetentionInss, payable.RetentionIr, payable.RetentionPcc) = (retentions.Iss, retentions.Inss, retentions.Ir, retentions.Pcc);
        payable.DueDate = input.DueDate;
        payable.IsEstimate = false;
        await db.SaveChangesAsync(cancellationToken);
        return (await DetailsAsync([payable], cancellationToken))[0];
    }

    /// <summary>Forma de pagamento (CA 6 e 7): trava em OP; toda troca grava a data, para o aviso da OP.</summary>
    public async Task<Result<PaymentDetailsView?>> SetPaymentMethodAsync(Guid id, PaymentMethodInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var payable = await db.Payables.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (payable is null)
        {
            return NotFound();
        }

        if (Stale(payable, input.Version) is { } stale)
        {
            return stale;
        }

        if (payable.InPaymentOrder)
        {
            return InOrder(payable);
        }

        if (payable.Status is PayableStatus.Cancelled or PayableStatus.Paid)
        {
            return Error.Conflict("payable_closed", "Título pago ou cancelado não muda.");
        }

        var (details, bad) = DetailsOf(input.PaymentDetails);
        if (bad is not null)
        {
            return bad;
        }

        Expect(payable, input.Version);
        payable.PaymentMethod = details?.Kind;
        payable.PaymentDetails = details is null ? null : PaymentDetails.Serialize(details);
        payable.PaymentMethodChangedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return Result<PaymentDetailsView?>.Success(await ViewAsync(payable, cancellationToken));
    }

    /// <summary>Cancelamento (CA 12 e 13): fora de OP, com motivo; acima do limite, só com <c>finance.approve</c>; avisa o Proprietário.</summary>
    public async Task<Result<PayableDetail>> CancelAsync(Guid id, ReasonInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var payable = await db.Payables.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (payable is null)
        {
            return NotFound();
        }

        if (Stale(payable, input.Version) is { } stale)
        {
            return stale;
        }

        if (payable.InPaymentOrder)
        {
            return InOrder(payable);
        }

        if (payable.Status is not (PayableStatus.Open or PayableStatus.Planned))
        {
            return Error.Conflict("payable_not_open", $"O título {payable.Number} não está em aberto.");
        }

        if (!EditableOrigins.Contains(payable.Origin))
        {
            return OriginProtected(payable);
        }

        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length > 200)
        {
            return ReceivableService.Invalid("reason", "Informe o motivo, com até 200 caracteres.");
        }

        var limits = await parameters.LimitsAsync(cancellationToken);
        // Título de implantação, com a implantação aberta, cancela sem o limite (FIN-00 CA 8).
        var openingFree = payable.Origin == DocumentOrigin.Opening && !await db.OpeningSetups.AnyAsync(s => s.CompletedAt != null, cancellationToken);
        if (!openingFree && payable.NetAmount > limits.CancelApprovalAmount && !await permissions.HasPermissionAsync("finance", "approve", cancellationToken))
        {
            return Error.Forbidden("cancel_requires_approval", $"Valor acima de {ReceivableService.Money(limits.CancelApprovalAmount)}: só quem aprova em Caixa e pagamentos pode cancelar.");
        }

        Expect(payable, input.Version);
        payable.Status = PayableStatus.Cancelled;
        payable.CancelReason = input.Reason.Trim();
        payable.CancelledAt = clock.GetUtcNow();
        payable.CancelledBy = currentUser.UserId;
        var who = currentUser.UserId is { } me ? await users.DisplayNameAsync(me, cancellationToken) ?? "um usuário" : "o sistema";
        foreach (var owner in await users.OwnerIdsAsync(cancellationToken))
        {
            notifications.Notify(owner, NotificationType, $"Título a pagar {payable.Number} de {ReceivableService.Money(payable.NetAmount)} cancelado por {who}: {payable.CancelReason}", "/app/financeiro/pagar/" + payable.Id);
        }

        await db.SaveChangesAsync(cancellationToken);
        return (await DetailsAsync([payable], cancellationToken))[0];
    }

    private async Task<Result<PayablesCreated>> SaveNewAsync(IReadOnlyList<(string Description, decimal Amount, DateOnly DueDate, Retentions Retentions)> parcels, PayableInput common, PaymentDetailsInput? details, Guid? groupId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var created = new List<Payable>();
        foreach (var (description, amount, dueDate, retentions) in parcels)
        {
            var payable = new Payable
            {
                Number = await numbers.NextAsync(DocType, ct), PartyId = common.PartyId, Description = description, AccountId = common.AccountId, CostCenterId = common.CostCenterId,
                Amount = amount, RetentionIss = retentions.Iss, RetentionInss = retentions.Inss, RetentionIr = retentions.Ir, RetentionPcc = retentions.Pcc,
                IssueDate = common.IssueDate, DueDate = dueDate, Status = PayableStatus.Open, Origin = DocumentOrigin.Manual, InstallmentGroupId = groupId,
                PaymentMethod = details?.Kind, PaymentDetails = details is null ? null : PaymentDetails.Serialize(details), PaymentMethodChangedAt = details is null ? null : clock.GetUtcNow(),
                CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
            };
            db.Payables.Add(payable);
            created.Add(payable);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        var view = await ViewAsync(created[0], ct);
        return new PayablesCreated(await DetailsAsync(created, ct), view?.HolderWarning, view?.BoletoWarning);
    }

    private (PaymentDetailsInput? Value, Error? Error) DetailsOf(PaymentDetailsInput? input) => input is null ? (null, null) : PaymentDetails.Validate(input, Today);

    private async Task<Error?> ValidateAsync(Guid partyId, string? description, Guid accountId, Guid? costCenterId, decimal amount, Retentions retentions, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > 200)
        {
            return ReceivableService.Invalid("description", "Informe a descrição, com até 200 caracteres.");
        }

        if (amount is <= 0 or > MaxAmount)
        {
            return ReceivableService.Invalid("amount", "Informe um valor maior que zero.");
        }

        foreach (var (value, field) in new[] { (retentions.Iss, "retentionIss"), (retentions.Inss, "retentionInss"), (retentions.Ir, "retentionIr"), (retentions.Pcc, "retentionPcc") })
        {
            if (value < 0)
            {
                return ReceivableService.Invalid(field, "A retenção não pode ser negativa.");
            }
        }

        if (retentions.Sum >= amount)
        {
            return ReceivableService.Invalid("retentionIss", "As retenções precisam ser menores que o valor.");
        }

        var party = await parties.FindAsync(partyId, ct);
        if (party is null || party.IsDeleted || !party.IsActive || !(party.Roles.Contains(PartyRoleKind.Supplier) || party.Roles.Contains(PartyRoleKind.Installer)))
        {
            return ReceivableService.Invalid("partyId", "Escolha um fornecedor ou instalador.");
        }

        var found = await chart.FindAsync([accountId], ct);
        var account = found.Count > 0 ? found[0] : null;
        if (account is null || !account.IsActive || account.Type == AccountType.Revenue)
        {
            return ReceivableService.Invalid("accountId", "Escolha uma conta de despesa, investimento ou financiamento ativa.");
        }

        if (!account.AcceptsEntries)
        {
            return ReceivableService.Invalid("accountId", "Escolha uma conta analítica.");
        }

        return costCenterId is { } center && !await db.CostCenters.AnyAsync(c => c.Id == center && c.IsActive, ct)
            ? ReceivableService.Invalid("costCenterId", "Escolha um centro de custo ativo.")
            : null;
    }

    private static Error OriginProtected(Payable payable) =>
        Error.Conflict("payable_origin", $"Título gerado por {OriginName(payable.Origin)} ({payable.Number}): ajuste na origem.");

    internal static string OriginName(DocumentOrigin origin) => origin switch
    {
        DocumentOrigin.Installer => "OS de instalação",
        DocumentOrigin.Purchase => "pedido de compra",
        DocumentOrigin.Payroll => "folha",
        DocumentOrigin.Maintenance => "manutenção",
        DocumentOrigin.Recurring => "despesa recorrente",
        _ => "outro módulo",
    };
}
