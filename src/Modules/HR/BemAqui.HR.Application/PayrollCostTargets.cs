using BemAqui.Finance.Contracts;
using BemAqui.HR.Domain;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>Conta do plano e centro de custo de um grupo (RH-02c CA 4).</summary>
public sealed record PayrollCostTarget(Guid AccountId, Guid? CostCenterId);

/// <summary>
/// O que o fechamento e a previsão precisam do Finance e do calendário (RH-02c CA 1 e 4): a conta e o centro de custo de cada grupo (pelos
/// códigos dos parâmetros do RH, "A confirmar com a contadora"), os vencimentos da folha e a data com o dia não útil antecipado.
/// </summary>
public sealed class PayrollCostTargets(IHrDb db, HrParametersService parameters, IChartOfAccounts chart, IBusinessCalendar calendar)
{
    public async Task<Result<Dictionary<PayrollCostGroup, PayrollCostTarget>>> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var accounts = await chart.PostableAsync(AccountType.Expense, cancellationToken);
        var centers = await chart.CostCentersAsync(cancellationToken);
        var result = new Dictionary<PayrollCostGroup, PayrollCostTarget>();
        foreach (var group in Enum.GetValues<PayrollCostGroup>())
        {
            var (accountCode, centerCode) = await parameters.CostCodesAsync(group, cancellationToken);
            if (accounts.FirstOrDefault(a => a.Code == accountCode) is not { } account)
            {
                return Error.Conflict("payroll_account_missing",
                    $"A conta {accountCode} da folha não está ativa no plano de contas. Ajuste em Configurações → Parâmetros → RH.");
            }

            result[group] = new PayrollCostTarget(account.Id, centers.FirstOrDefault(c => c.Code == centerCode)?.Id);
        }

        return result;
    }

    public Task<PayrollDueParameters> DueParametersAsync(CancellationToken cancellationToken = default) => parameters.DueParametersAsync(cancellationToken);

    /// <summary>O vencimento padrão do tipo já foi conferido pela contadora (chip "A confirmar com a contadora" no diálogo, CA 1).</summary>
    public async Task<bool> DueConfirmedAsync(PayrollRunType type, CancellationToken cancellationToken = default)
    {
        var key = type switch
        {
            PayrollRunType.Monthly => HrParametersService.SalaryDueBusinessDay,
            PayrollRunType.Advance => HrParametersService.AdvanceDueDay,
            PayrollRunType.Thirteenth1 => HrParametersService.ThirteenthFirstDue,
            PayrollRunType.Thirteenth2 => HrParametersService.ThirteenthSecondDue,
            _ => null,
        };
        return key is null || await db.HrParameters.AsNoTracking().AnyAsync(p => p.Key == key && p.ConfirmedAt != null, cancellationToken);
    }

    /// <summary>A data pela regra, com os dias úteis do calendário padrão (dia não útil antecipa).</summary>
    public async Task<DateOnly?> PaymentDateAsync(PaymentDateRule rule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (PayrollClosingRules.Range(rule) is not { } range)
        {
            return null;
        }

        var working = (await calendar.WorkingDatesAsync(range.From, range.To, cancellationToken)).ToHashSet();
        return PayrollClosingRules.Resolve(rule, working.Contains);
    }

    /// <summary>A data com o dia não útil antecipado.</summary>
    public Task<DateOnly> PreviousBusinessDayAsync(DateOnly date, CancellationToken cancellationToken = default) => calendar.PreviousBusinessDayAsync(date, cancellationToken);
}
