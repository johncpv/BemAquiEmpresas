using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

public sealed partial class OpeningService
{
    /// <summary>A receber em aberto na data de corte (CA 5 e 7): um título por linha, tudo ou nada.</summary>
    public async Task<Result<OpeningBatchResult>> CreateReceivablesAsync(OpeningReceivablesInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await GuardAsync(input.Lines.Count, cancellationToken) is { } blocked)
        {
            return blocked;
        }

        var cutover = (await db.OpeningSetups.AsNoTracking().FirstAsync(cancellationToken)).CutoverDate!.Value;
        var revenue = await chart.PostableAsync(AccountType.Revenue, cancellationToken);
        var fallback = revenue.FirstOrDefault(a => a.Code == "1.9")?.Id;
        var created = new List<Receivable>();
        for (var i = 0; i < input.Lines.Count; i++)
        {
            var line = input.Lines[i];
            if (await LineErrorAsync(i, line.PartyId, PartyRoleKind.Customer, line.Description, line.Reference, line.Balance, line.CostCenterId, cancellationToken) is { } bad)
            {
                return bad;
            }

            var account = line.AccountId ?? fallback;
            if (account is null || !revenue.Any(a => a.Id == account))
            {
                return ReceivableService.Invalid($"lines[{i}].accountId", "Escolha uma conta de receita analítica.");
            }

            created.Add(new Receivable
            {
                PartyId = line.PartyId, Description = line.Description.Trim(), OpeningReference = Trim(line.Reference), AccountId = account.Value, CostCenterId = line.CostCenterId,
                Amount = line.Balance, IssueDate = line.IssueDate ?? cutover, DueDate = line.DueDate, Status = ReceivableStatus.Open, Origin = DocumentOrigin.Opening,
                CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
            });
        }

        return await SaveAsync(created, OpeningBatchKind.Receivables, r => r.Amount, (r, n) => r.Number = n, ReceivableService.DocType, db.Receivables, cancellationToken);
    }

    /// <summary>A pagar em aberto (CA 6 e 7): o saldo líquido, sem retenções; conta padrão a última do fornecedor, senão "3.9".</summary>
    public async Task<Result<OpeningBatchResult>> CreatePayablesAsync(OpeningPayablesInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await GuardAsync(input.Lines.Count, cancellationToken) is { } blocked)
        {
            return blocked;
        }

        var cutover = (await db.OpeningSetups.AsNoTracking().FirstAsync(cancellationToken)).CutoverDate!.Value;
        var postable = (await chart.PostableAsync(null, cancellationToken)).Where(a => a.Type != AccountType.Revenue).ToList();
        var fallback = postable.FirstOrDefault(a => a.Code == "3.9")?.Id;
        var created = new List<Payable>();
        for (var i = 0; i < input.Lines.Count; i++)
        {
            var line = input.Lines[i];
            if (await LineErrorAsync(i, line.PartyId, null, line.Description, line.Reference, line.Balance, line.CostCenterId, cancellationToken) is { } bad)
            {
                return bad;
            }

            var account = line.AccountId
                ?? await db.Payables.Where(p => p.PartyId == line.PartyId).OrderByDescending(p => p.CreatedAt).Select(p => (Guid?)p.AccountId).FirstOrDefaultAsync(cancellationToken)
                ?? fallback;
            if (account is null || !postable.Any(a => a.Id == account))
            {
                return ReceivableService.Invalid($"lines[{i}].accountId", "Escolha uma conta de despesa, investimento ou financiamento analítica.");
            }

            PaymentDetailsInput? details = null;
            if (line.PaymentDetails is { } raw)
            {
                var (value, error) = PaymentDetails.Validate(raw, Today);
                if (error is not null)
                {
                    var field = error.Fields?.Keys.FirstOrDefault() ?? "paymentDetails";
                    return ReceivableService.Invalid($"lines[{i}].{field}", error.Message);
                }

                details = value;
            }

            created.Add(new Payable
            {
                PartyId = line.PartyId, Description = line.Description.Trim(), OpeningReference = Trim(line.Reference), AccountId = account.Value, CostCenterId = line.CostCenterId,
                Amount = line.Balance, IssueDate = cutover, DueDate = line.DueDate, Status = PayableStatus.Open, Origin = DocumentOrigin.Opening,
                PaymentMethod = details?.Kind, PaymentDetails = details is null ? null : PaymentDetails.Serialize(details), PaymentMethodChangedAt = details is null ? null : clock.GetUtcNow(),
                CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
            });
        }

        return await SaveAsync(created, OpeningBatchKind.Payables, p => p.Amount, (p, n) => p.Number = n, PayableService.DocType, db.Payables, cancellationToken);
    }
}
