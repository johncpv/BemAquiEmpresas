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
public sealed record FinanceLimits(decimal DiscountApprovalPct, decimal DiscountApprovalAmount, decimal CancelApprovalAmount, int BackdateDays, bool OwnerCanApproveOwn = false, decimal PaymentTolerance = 0, decimal TransferNotifyAmount = 10_000);

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
public sealed record ContractReceivable(Guid Id, string Number, Guid MilestoneId, ReceivableStatus Status, DateOnly? DueDate, decimal Amount, decimal Balance, bool HasSettlement);

/// <summary>Implantação financeira (FIN-00) vista pelos outros módulos: a data de corte limita o "recebido antes" (COM-04c CA 1, 3, 7).</summary>
public sealed record FinancialOpeningInfo(DateOnly? CutoverDate, DateTimeOffset? CompletedAt);

public interface IFinancialOpening
{
    Task<FinancialOpeningInfo> StatusAsync(CancellationToken cancellationToken = default);
}
