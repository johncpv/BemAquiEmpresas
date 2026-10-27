using System.Globalization;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.HR.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Salário na OP (RH-02c CA 8 a 11, ADR 0036 §3): o retrato guarda só o favorecido, a versão da conta e o final mascarado; a conta em
/// quarentena não entra; a conta em claro só é lida no "Pagar", pelo <see cref="IEmployeeBankAccounts"/>, conferindo a versão do retrato;
/// e quem fechou a folha não registra o pagamento dela (exceção do Proprietário único, com a marca "autopagamento").
/// </summary>
public sealed partial class PaymentOrderService
{
    public const string QuarantineCode = "payee_account_in_quarantine";
    public const string AccountChangedCode = "payee_account_changed";
    public const string CloserCannotPayCode = "payroll_closer_cannot_pay";

    private IEmployeeBankAccounts? BankAccounts => employeeAccounts.FirstOrDefault();

    /// <summary>Resumo da conta de salário dos funcionários dos títulos (sem dado bancário em claro).</summary>
    private async Task<Dictionary<Guid, EmployeeBankAccountSummary>> EmployeeAccountsAsync(IEnumerable<Payable> payables, CancellationToken ct)
    {
        var ids = payables.Where(p => p.IsEmployeePayee).Select(p => p.PayeeId).Distinct().ToList();
        if (ids.Count == 0 || BankAccounts is not { } accounts)
        {
            return [];
        }

        return (await accounts.SummaryAsync(ids, ct)).ToDictionary(s => s.EmployeeId);
    }

    /// <summary>Sem conta ou conta em quarentena (CA 9): 422, e o título fica fora da OP.</summary>
    private static Error? EmployeeItemError(int i, Payable payable, Dictionary<Guid, EmployeeBankAccountSummary> accounts)
    {
        if (!payable.IsEmployeePayee)
        {
            return null;
        }

        if (!accounts.TryGetValue(payable.PayeeId, out var account) || account.Version == 0)
        {
            return ReceivableService.Invalid($"items[{i}].payableId", $"O funcionário do título {payable.Number} não tem conta para pagamento. Cadastre a conta no RH.");
        }

        if (account.InQuarantine)
        {
            var who = account.Registration ?? account.Name;
            var until = account.QuarantineUntil is { } u ? BusinessTime.ToBusiness(u).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "a revisão";
            var message = $"A conta de {who} está em quarentena até {until}. Peça a revisão da conta no RH.";
            return Error.Validation(QuarantineCode, message, new Dictionary<string, string[]> { [$"items[{i}].payableId"] = [message] });
        }

        return null;
    }

    private static PayeeSnapshot EmployeeSnapshot(Payable payable, EmployeeBankAccountSummary account, PaymentOrderItem item) => new(
        null, string.IsNullOrWhiteSpace(account.Registration) ? account.Name : $"{account.Registration} — {account.Name}", null, null, payable.Number, payable.Description,
        payable.NetAmount, payable.Retentions, item.Amount, item.ExpectedCharges, item.ApprovedTotal, payable.DueDate,
        new PaymentDetailsInput(PaymentMethodKind.EmployeeAccount, Account: account.AccountTail), null,
        PayeeType.Employee, payable.PayeeId, account.Registration, account.Version, account.AccountTail, account.NewOnPayroll, account.ReviewedAt);

    /// <summary>Avisos do salário no diálogo de aprovação (CA 8).</summary>
    private static IEnumerable<string> EmployeeWarnings(PayeeSnapshot s)
    {
        if (s.NewOnPayroll)
        {
            yield return "Novo na folha.";
        }

        if (s.AccountReviewedAt is { } reviewed)
        {
            yield return $"Conta revisada em {BusinessTime.ToBusiness(reviewed).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}.";
        }
    }

    /// <summary>
    /// "Para onde vai o dinheiro" (CA 10): os dados bancários dos salários da OP aprovada, lidos do RH só agora (com <c>SensitiveRead</c>
    /// e o número da OP, nada gravado no Finance). A versão diferente da do retrato responde 409 <c>payee_account_changed</c>.
    /// </summary>
    public async Task<Result<IReadOnlyList<PayeeAccountView>>> PayeeAccountsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await db.PaymentOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Status != PaymentOrderStatus.Approved)
        {
            return Error.Conflict("payment_order_not_approved", "Só é possível pagar uma ordem aprovada.");
        }

        var result = new List<PayeeAccountView>();
        var items = await db.PaymentOrderItems.AsNoTracking().Where(i => i.PaymentOrderId == id).OrderBy(i => i.Id).ToListAsync(cancellationToken);
        foreach (var item in items)
        {
            var s = Snapshot(item);
            if (s.PayeeType != PayeeType.Employee || s.EmployeeId is not { } employeeId)
            {
                continue;
            }

            if (BankAccounts is not { } accounts)
            {
                return Error.Conflict(AccountChangedCode, "A conta do funcionário não está disponível. Tente de novo.");
            }

            var read = await accounts.GetForPaymentAsync(employeeId, s.BankAccountVersion ?? 0, order.Id, order.Number, cancellationToken);
            if (read.IsFailure)
            {
                return read.Error!;
            }

            var a = read.Value;
            result.Add(new PayeeAccountView(item.Id, s.PayableNumber, a.Name, a.Registration, a.BankCode, a.BankCode is { } code ? Banks.All.FirstOrDefault(b => b.Code == code)?.Name : null,
                a.Agency, a.Account, a.AccountType, a.HolderName, a.HolderCpf, a.PixKeyType, a.PixKey));
        }

        return result;
    }

    /// <summary>No "Pagar" (CA 10): a conta de cada salário ainda é a do retrato (só a versão, sem ler o dado bancário).</summary>
    private async Task<Error?> EmployeeAccountsChangedAsync(IReadOnlyList<PaymentOrderItem> items, CancellationToken ct)
    {
        var snapshots = items.Select(Snapshot).Where(s => s.PayeeType == PayeeType.Employee && s.EmployeeId is not null).ToList();
        if (snapshots.Count == 0)
        {
            return null;
        }

        var current = BankAccounts is { } accounts ? (await accounts.SummaryAsync(snapshots.Select(s => s.EmployeeId!.Value).Distinct().ToList(), ct)).ToDictionary(a => a.EmployeeId) : [];
        foreach (var s in snapshots)
        {
            if (!current.TryGetValue(s.EmployeeId!.Value, out var now) || now.Version != s.BankAccountVersion)
            {
                return Error.Conflict(AccountChangedCode, $"A conta de {s.Registration ?? s.PartyName} mudou depois do envio. Envie para nova aprovação.");
            }
        }

        return null;
    }

    /// <summary>
    /// Quem fechou a folha não paga (CA 11): o <c>created_by</c> dos títulos de origem <c>Payroll</c> é quem fechou. Exceção: o Proprietário
    /// único, com a marca "autopagamento". Devolve o erro, ou se é autopagamento.
    /// </summary>
    private async Task<(Error? Error, bool SelfPaid)> PayrollCloserRuleAsync(IReadOnlyList<Payable> payables, CancellationToken ct)
    {
        var me = currentUser.UserId;
        if (me is null || !payables.Any(p => p.Origin == DocumentOrigin.Payroll && p.CreatedBy == me))
        {
            return (null, false);
        }

        var owners = await users.OwnerIdsAsync(ct);
        return owners.Count == 1 && owners.Contains(me.Value)
            ? (null, true)
            : (Error.Forbidden(CloserCannotPayCode, "Quem fechou a folha não registra o pagamento dela."), false);
    }
}
