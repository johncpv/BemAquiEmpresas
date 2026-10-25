namespace BemAqui.Finance.Contracts;

// Resumos do Finance para o painel do proprietário (DSH-01a, ADR 0028 §1): cada um é o mesmo serviço do relatório de origem,
// então o número do card é igual ao da tela do Financeiro. Sem custo nem margem (ADR 0028 §2).

/// <summary>Saldo atual de uma conta ativa (FIN-05 <c>BankBalanceQuery</c>, o mesmo da tela Contas e extrato).</summary>
public sealed record BankBalanceSummaryAccount(Guid Id, string Name, decimal Balance, bool OpeningMissing);

/// <summary>Saldo consolidado das contas ativas no fim de <see cref="Date"/> (hoje, em America/Sao_Paulo).</summary>
public sealed record BankBalancesSummary(DateOnly Date, decimal Consolidated, IReadOnlyList<BankBalanceSummaryAccount> Accounts);

public interface IBankBalances
{
    Task<BankBalancesSummary> CurrentAsync(CancellationToken cancellationToken = default);
}

/// <summary>Faixa do aging (FIN-06a CA 1): "0–30", "31–60", "61–90" e "90+".</summary>
public sealed record AgingBucketSummary(string Label, int FromDays, int? ToDays, decimal Total, int Count);

/// <summary>Cliente com saldo vencido, com o atraso médio dos últimos 12 meses (FIN-06b); nulo sem histórico.</summary>
public sealed record AgingCustomerSummary(Guid PartyId, string PartyName, decimal Total, int MaxDaysLate, decimal? AvgDelayDays);

/// <summary>Maior título vencido (pelo saldo), para a faixa "Atenção" do painel (DSH-01a CA 10).</summary>
public sealed record LargestOverdueReceivable(Guid ReceivableId, string Number, Guid PartyId, string PartyName, decimal Balance, int DaysLate, decimal? CustomerAvgDelayDays);

/// <summary>Resumo da inadimplência: os mesmos totais e faixas da tela Inadimplência, os maiores clientes e o maior atraso.</summary>
public sealed record ReceivablesAgingSummary(
    DateOnly Today, decimal Total, int Count, int Customers, IReadOnlyList<AgingBucketSummary> Buckets, IReadOnlyList<AgingCustomerSummary> TopCustomers,
    int MaxDaysLate, LargestOverdueReceivable? Largest);

public interface IReceivablesAging
{
    /// <summary>Resumo do aging de hoje; <paramref name="top"/> maiores clientes pelo saldo vencido.</summary>
    Task<ReceivablesAgingSummary> SummaryAsync(int top = 5, CancellationToken cancellationToken = default);
}

/// <summary>Meta de faturamento de um mês (<c>finance.revenue_targets</c>); nula quando o mês não tem meta.</summary>
public sealed record RevenueTargetMonth(int Month, decimal? Amount);

/// <summary>Metas dos 12 meses do ano; <see cref="YearTotal"/> é a soma dos meses com meta, nula se nenhum tem.</summary>
public sealed record RevenueTargetsYear(int Year, IReadOnlyList<RevenueTargetMonth> Months, decimal? YearTotal);

/// <summary>Metas de um ano gravadas (DSH-01a CA 9): o painel do proprietário recalcula o card de faturamento.</summary>
public sealed record RevenueTargetsChanged(int Year) : BemAqui.SharedKernel.IIntegrationEvent;

/// <summary>Metas de faturamento (DSH-01a CA 9, ADR 0028 §1), donas do Finance, editadas com <c>settings.edit</c>.</summary>
public interface IRevenueTargets
{
    Task<RevenueTargetsYear> ForYearAsync(int year, CancellationToken cancellationToken = default);
}
