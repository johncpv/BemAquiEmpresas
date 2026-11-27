using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Contracts;

/// <summary>Situação do boleto do pedido como o Sales mostra (VND-02a CA 15 e 18).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OrderSlipStatus>))]
public enum OrderSlipStatus
{
    /// <summary>Pedido ao Inter, aguardando a emissão.</summary>
    Requested,
    Issued,
    Failed,
    Paid,

    /// <summary>Pago com diferença: aguardando a conferência do Financeiro (BNK-02 CA 9).</summary>
    PaidPendingReview,
    Cancelled,
    Expired,
}

/// <summary>
/// Cobrança do pedido (VND-02a CA 15): o pedido, o número dele, o cliente, o valor, o vencimento (<c>date</c>) e se o e-mail sai na emissão. Multa e
/// juros vêm de Configurações → Cobrança; a conta, da integração Inter padrão.
/// </summary>
public sealed record OrderChargeRequest(Guid OrderId, string OrderNumber, Guid CustomerId, decimal Amount, DateOnly DueDate, bool SendEmail, Guid? ClientRequestId = null);

/// <summary>Boleto do título do pedido: só o que a aba Cobrança mostra (linha digitável, Pix copia-e-cola, QR, situação e datas).</summary>
public sealed record OrderSlipView(
    Guid Id, OrderSlipStatus Status, decimal Amount, DateOnly DueDate, string? DigitableLine, string? PixCopyPaste, string? QrCode, bool HasPdf, string? FailureReason, bool CancelPending,
    DateTimeOffset RequestedAt, DateTimeOffset? IssuedAt, DateTimeOffset? PaidAt);

/// <summary>Título <c>SalesOrder</c> do pedido e o boleto atual (o mais novo). <c>RefundedAt</c>: devolução registrada (CA 19 e 26).</summary>
public sealed record OrderChargeView(
    Guid ReceivableId, string ReceivableNumber, ReceivableStatus Status, decimal Amount, decimal PaidAmount, DateOnly? DueDate, DateOnly? SettledOn, OrderSlipView? Slip,
    DateTimeOffset? RefundedAt);

/// <summary>O que falta para cobrar o cliente por boleto (CA 17): documento, endereço de cobrança; e se a integração Inter está pronta.</summary>
public sealed record OrderChargeReadiness(bool IntegrationReady, bool HasDocument, bool HasBillingAddress, string? Email, IReadOnlyList<string> Missing)
{
    public bool CustomerBillable => HasDocument && HasBillingAddress;
}

/// <summary>Reconsulta do boleto no Inter antes do registro à mão (CA 20): pago lá (com a data) ou não.</summary>
public sealed record OrderSlipRecheck(bool Paid, DateOnly? PaidOn);

/// <summary>
/// Cobrança do pedido de venda pelo Financeiro (VND-02a, ADR 0052 §1): o título <c>SalesOrder</c> contra o cliente (um por pedido, índice único
/// parcial do título ativo) e o boleto com Pix pelo fluxo da BNK-01b. Fora da régua de cobrança e da receita por título.
/// </summary>
public interface IOrderCharges
{
    /// <summary>Documento, endereço e integração para o diálogo "Gerar cobrança" (CA 15 e 17).</summary>
    Task<OrderChargeReadiness> ReadinessAsync(Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cria o título (ou reaproveita o ativo do pedido) e pede o boleto (<c>Requested → Issued</c> pelo job). Grava em transação própria: não
    /// chame dentro de outra. Repetir com o mesmo pedido devolve a cobrança existente.
    /// </summary>
    Task<Result<OrderChargeView>> ChargeAsync(OrderChargeRequest request, CancellationToken cancellationToken = default);

    /// <summary>O título ativo do pedido (ou o último), com o boleto; null sem cobrança.</summary>
    Task<OrderChargeView?> FindAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Reconsulta o boleto aberto no Inter (CA 20). Sem boleto emitido: não pago.</summary>
    Task<Result<OrderSlipRecheck>> RecheckSlipAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancela a cobrança do pedido (CA 20 e 25): o boleto aberto vai ao Inter pelo job e o título é cancelado quando o banco confirma (se o pagamento
    /// chegar antes, a baixa segue e o Sales trata como pago depois de cancelado, CA 19); sem boleto aberto, o título é cancelado já. Não salva: quem
    /// chama grava na própria transação.
    /// </summary>
    Task CancelAsync(Guid orderId, string reason, bool paidOtherMeans = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registra a devolução do valor pago pelo boleto (CA 19 e 26): a saída na conta do recebimento, com o título encerrado como devolvido. Idempotente.
    /// Não salva.
    /// </summary>
    Task<Result> RefundAsync(Guid orderId, string reason, CancellationToken cancellationToken = default);
}
