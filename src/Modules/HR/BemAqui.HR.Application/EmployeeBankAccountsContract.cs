using System.Globalization;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// <see cref="IEmployeeBankAccounts"/> (RH-02c CA 8 a 10, ADR 0036 §3): o Finance nunca copia a conta de salário. O resumo sai sem dado
/// bancário em claro (o final mascarado e a versão, para o retrato da OP); os dados para pagar saem só com <c>finance.pay</c> no request,
/// com a versão do retrato e fora da quarentena, e cada leitura grava <c>SensitiveRead</c> com o número da OP.
/// </summary>
public sealed class EmployeeBankAccountsContract(IHrDb db, IPermissionChecker permissions, IAuditWriter audit, TimeProvider clock) : IEmployeeBankAccounts
{
    public const string ChangedCode = "payee_account_changed";
    public const string QuarantineCode = "payee_account_in_quarantine";

    public async Task<IReadOnlyList<EmployeeBankAccountSummary>> SummaryAsync(IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employeeIds);
        var ids = employeeIds.Distinct().ToList();
        var now = clock.GetUtcNow();
        var rows = await db.Employees.IgnoreQueryFilters().AsNoTracking().Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, e.Name, e.Registration, e.BankAccountVersion, e.BankAccount, e.PixKeyType, e.BankQuarantineUntil, e.BankReviewedAt }).ToListAsync(cancellationToken);
        // "Novo na folha": nenhum holerite em outra folha fechada ou paga. O salário pago vem de uma folha fechada, que já conta uma vez;
        // veterano é quem aparece em duas ou mais.
        var closedRuns = (from p in db.Payslips.AsNoTracking()
                          join r in db.PayrollRuns.AsNoTracking() on p.PayrollRunId equals r.Id
                          where ids.Contains(p.EmployeeId) && r.Status != PayrollRunStatus.Draft
                          select new { p.EmployeeId, RunId = r.Id }).Distinct();
        var veterans = (await (from x in closedRuns
                               group x by x.EmployeeId into g
                               where g.Count() > 1
                               select g.Key).ToListAsync(cancellationToken)).ToHashSet();
        return rows.Select(r => new EmployeeBankAccountSummary(r.Id, r.Name, r.Registration, r.BankAccountVersion, Tail(r.BankAccount, r.PixKeyType), !veterans.Contains(r.Id),
            EmployeeBankRules.InQuarantine(r.BankQuarantineUntil, r.BankReviewedAt, now), r.BankQuarantineUntil, r.BankReviewedAt)).ToList();
    }

    public async Task<Result<EmployeeBankAccountForPayment>> GetForPaymentAsync(Guid employeeId, int expectedVersion, Guid paymentOrderId, string paymentOrderNumber, CancellationToken cancellationToken = default)
    {
        if (!await permissions.HasPermissionAsync("finance", "pay", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Só quem paga lê a conta do funcionário.");
        }

        var e = await db.Employees.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.Id == employeeId, cancellationToken);
        if (e is null || e.BankAccountVersion == 0)
        {
            return Error.NotFound("employee_bank_account_not_found", "O funcionário não tem conta para pagamento.");
        }

        var who = e.Registration ?? e.Name;
        if (e.BankAccountVersion != expectedVersion)
        {
            return Error.Conflict(ChangedCode, $"A conta de {who} mudou depois do envio. Envie para nova aprovação.");
        }

        if (EmployeeBankRules.InQuarantine(e.BankQuarantineUntil, e.BankReviewedAt, clock.GetUtcNow()))
        {
            var until = BusinessTime.ToBusiness(e.BankQuarantineUntil!.Value).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            return Error.Validation(QuarantineCode, $"A conta de {who} está em quarentena até {until}. Peça a revisão da conta no RH.");
        }

        await audit.RecordReadAsync(nameof(Employee) + ".BankAccount", employeeId.ToString(), $"{paymentOrderNumber} ({paymentOrderId})", cancellationToken);
        return new EmployeeBankAccountForPayment(e.Id, e.Name, e.Registration, e.BankAccountVersion, e.BankCode, e.BankAgency, e.BankAccount, e.BankAccountType?.ToString(), e.BankHolderName,
            e.BankHolderCpf, e.PixKeyType?.ToString(), e.PixKey);
    }

    /// <summary>"****-12" (os 2 últimos da conta) ou, só com Pix, "Pix {tipo}".</summary>
    private static string? Tail(string? account, EmployeePixKeyType? pixType) =>
        account is { Length: > 0 } a ? "****-" + (a.Length <= 2 ? a : a[^2..]) : pixType is { } t ? "Pix " + t : null;
}
