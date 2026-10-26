using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Contracts;

/// <summary>Situação gravada do título a receber (04-fluxos §1); "Vencido" é calculado, nunca gravado (FIN-01 CA 6).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReceivableStatus>))]
public enum ReceivableStatus
{
    /// <summary>Marco por evento ainda sem data (COM-04b).</summary>
    Planned,
    Open,
    PartiallyPaid,
    Paid,
    Cancelled,
}

/// <summary>Movimento numa conta bancária: entrada (+) ou saída (−); tipos novos entram com a FIN-03a e a FIN-04.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TransactionType>))]
public enum TransactionType
{
    Receipt,
    Payment,
    Transfer,
    BankEntry,
}

/// <summary>
/// Baixa, quitação ou estorno de um título a receber (FIN-01): sem dado pessoal. A COM-04b conclui o contrato com ele; o
/// DSH-02 (Fase 4) atualiza o painel.
/// </summary>
public sealed record ReceivableSettled(
    Guid ReceivableId,
    Guid SettlementId,
    Guid PartyId,
    Guid? ProjectId,
    DateOnly? DueDate,
    DateOnly SettledOn,
    decimal Principal,
    bool IsReversal) : IIntegrationEvent;

/// <summary>Parâmetros do Finance (ADR 0016 adendo M6): limites de desconto, cancelamento e data retroativa (FIN-01 CA 13, 14 e 18).</summary>
public sealed record FinanceLimits(
    decimal DiscountApprovalPct, decimal DiscountApprovalAmount, decimal CancelApprovalAmount, int BackdateDays, bool OwnerCanApproveOwn = false, decimal PaymentTolerance = 0, decimal TransferNotifyAmount = 10_000,
    decimal AdjustedDefaultThresholdPct = 20);

/// <summary>Leitura dos parâmetros do Finance pelos outros módulos e telas (Configurações → Parâmetros, seção Financeiro).</summary>
public interface IFinanceParameters
{
    Task<FinanceLimits> LimitsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Títulos a receber vistos por quem os gerou (COM-04a CA 11: "Gerando parcelas…" até eles existirem; COM-04b).</summary>
public interface IReceivables
{
    Task<int> CountByOriginAsync(DocumentOrigin origin, Guid originId, CancellationToken cancellationToken = default);

    /// <summary>Parcelas do contrato (COM-04b CA 14 e 16), na ordem do vencimento.</summary>
    Task<IReadOnlyList<ContractReceivable>> ForContractAsync(Guid contractId, CancellationToken cancellationToken = default);

    /// <summary>Títulos a receber do projeto (COM-05), na ordem do vencimento.</summary>
    Task<IReadOnlyList<ContractReceivable>> ByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);
}

/// <summary>Parcela do contrato vista pelo Commercial (COM-04b CA 14 e 16): o marco é o <c>origin_id</c>.</summary>
public sealed record ContractReceivable(
    Guid Id, string Number, Guid MilestoneId, ReceivableStatus Status, DateOnly? DueDate, decimal Amount, decimal Balance, bool HasSettlement, string? Description = null, DateTimeOffset? CreatedAt = null);

/// <summary>Implantação financeira (FIN-00) vista pelos outros módulos: a data de corte limita o "recebido antes" (COM-04c CA 1, 3, 7).</summary>
public sealed record FinancialOpeningInfo(DateOnly? CutoverDate, DateTimeOffset? CompletedAt);

public interface IFinancialOpening
{
    Task<FinancialOpeningInfo> StatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Pontualidade do cliente (FIN-06b CA 2, 3; R-TER-4): atraso médio ponderado pelo valor e % pago com atraso nos últimos 12
/// meses; sem histórico, a média do tenant serve de referência (FIN-07, Fase 4).
/// </summary>
public sealed record CustomerPaymentStats(
    decimal AvgDelayDays, decimal PctPaidLate, decimal PctOverdueNow, decimal Last12mInvoiced, bool HasHistory, decimal TenantAvgDelayDays);

/// <summary>
/// Pontualidade e inadimplência do cliente para a projeção ajustada (FIN-07 CA 2, 3 e 16). <c>DefaultPct</c> = min(100,
/// <c>Overdue30Amount</c> ÷ <c>Last12mInvoiced</c> × 100), com 2 casas; o vencido há mais de 30 dias inclui os títulos
/// <c>Opening</c>, o faturado não.
/// </summary>
public sealed record CustomerDefaultStats(
    Guid PartyId, decimal AvgDelayDays, bool HasHistory, decimal TenantAvgDelayDays, decimal Overdue30Amount, decimal Last12mInvoiced, decimal DefaultPct, bool TenantHasHistory);

public interface ICustomerPaymentStats
{
    Task<CustomerPaymentStats> ForPartyAsync(Guid partyId, CancellationToken cancellationToken = default);

    /// <summary>Vários clientes de uma vez, num número fixo de consultas, sem N+1 (FIN-07 CA 16). Cliente sem título vem zerado.</summary>
    Task<IReadOnlyDictionary<Guid, CustomerDefaultStats>> ForPartiesAsync(IReadOnlyCollection<Guid> partyIds, CancellationToken cancellationToken = default);
}

/// <summary>Um cenário do caixa projetado: saldo no fim do horizonte e o menor saldo (data e valor).</summary>
public sealed record CashForecastScenario(decimal EndBalance, DateOnly? LowestDate, decimal? LowestBalance);

/// <summary>
/// Resumo do caixa projetado para o painel do dono (FIN-07 CA 14, DSH-01a): saldo atual consolidado, base e ajustado, e a
/// diferença causada pelos atrasos (menor saldo base − menor saldo ajustado). Os números são os da tela do fluxo de caixa.
/// </summary>
public sealed record CashForecastSummary(DateOnly Today, int HorizonDays, decimal CurrentBalance, CashForecastScenario Base, CashForecastScenario Adjusted, decimal LowestDifference);

public interface ICashForecast
{
    /// <summary>Horizonte de 30, 60 ou 90 dias (até 365).</summary>
    Task<CashForecastSummary> SummaryAsync(int horizonDays, CancellationToken cancellationToken = default);
}
