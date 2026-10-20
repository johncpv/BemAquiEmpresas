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
public sealed record FinanceLimits(decimal DiscountApprovalPct, decimal DiscountApprovalAmount, decimal CancelApprovalAmount, int BackdateDays, bool OwnerCanApproveOwn = false, decimal PaymentTolerance = 0);

/// <summary>Leitura dos parâmetros do Finance pelos outros módulos e telas (Configurações → Parâmetros, seção Financeiro).</summary>
public interface IFinanceParameters
{
    Task<FinanceLimits> LimitsAsync(CancellationToken cancellationToken = default);
}
