using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Contracts;

/// <summary>
/// Situação gravada do título a pagar (04-fluxos §1). A FIN-02 grava só Planned, Open e Cancelled; AwaitingApproval,
/// Approved, PartiallyPaid e Paid são da ordem de pagamento (FIN-03a). "Vencido" é calculado.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PayableStatus>))]
public enum PayableStatus
{
    Planned,
    Open,
    AwaitingApproval,
    Approved,
    PartiallyPaid,
    Paid,
    Cancelled,
}

/// <summary>Forma de pagamento do título (D9): para o aprovador e o pagador verem para onde o dinheiro vai.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PaymentMethodKind>))]
public enum PaymentMethodKind
{
    Pix,
    Boleto,
    Ted,
    Other,
}

[JsonConverter(typeof(JsonStringEnumConverter<PixKeyType>))]
public enum PixKeyType
{
    Cpf,
    Cnpj,
    Email,
    Phone,
    Random,
}

/// <summary>Título a pagar visto por quem o gerou (COM-06, compras, folha).</summary>
/// <param name="PaymentOrderId">A OP não finalizada em que o título está (só no <c>ListByOriginAsync</c>), para o link "Ver OP".</param>
public sealed record PayableRef(Guid Id, string Number, PayableStatus Status, decimal Amount, DateOnly? DueDate, bool IsEstimate, short Installment = 1, Guid? PaymentOrderId = null);

/// <summary>
/// Contas a pagar para os outros módulos (FIN-02 CA 14 e 15): o título nasce na transação do chamador (o chamador grava),
/// idempotente por <c>(origin, originId, installment)</c> (ADR 0024 §2); os métodos de uma parcela valem com <c>installment = 1</c>.
/// Título já numa OP recusa mudar ou cancelar (<c>payable_in_payment_order</c>).
/// </summary>
public interface IPayables
{
    /// <param name="planned">True: nasce Planned (sem vencimento, ou com a data estimada e a marca de estimativa).</param>
    Task<Result<PayableRef>> CreateFromOriginAsync(
        DocumentOrigin origin, Guid originId, Guid partyId, Guid? projectId, decimal amount, DateOnly? dueDate, bool estimatedDueDate, string description, bool planned,
        CancellationToken cancellationToken = default);

    Task<Result<PayableRef>> ConfirmAsync(DocumentOrigin origin, Guid originId, decimal amount, DateOnly dueDate, CancellationToken cancellationToken = default);

    Task<Result<PayableRef>> UpdatePlannedAsync(DocumentOrigin origin, Guid originId, decimal amount, DateOnly? dueDate, CancellationToken cancellationToken = default);

    Task<Result> CancelFromOriginAsync(DocumentOrigin origin, Guid originId, string reason, CancellationToken cancellationToken = default);

    /// <summary>Título gerado por uma origem (COM-06: o título do instalador na OS); null se não existe.</summary>
    Task<PayableRef?> FindByOriginAsync(DocumentOrigin origin, Guid originId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cria os títulos <c>Planned</c> por parcela, com a data estimada e a marca "Estimado" (ADR 0024 §2, ADR 0019 §17), na transação do
    /// chamador; idempotente por parcela (a parcela que já tem título não cancelado não nasce de novo). Conta e natureza vêm do chamador.
    /// </summary>
    Task<Result<IReadOnlyList<PayableRef>>> CreatePlannedInstallmentsAsync(DocumentOrigin origin, Guid originId, PlannedPayables plan, CancellationToken cancellationToken = default);

    /// <summary>
    /// Troca os títulos <c>Planned</c> ainda não confirmados da origem pelas parcelas novas (reaprovação, replanejamento do saldo): a parcela
    /// que já existe é atualizada, a que sobra é cancelada e a que falta é criada. 409 <c>payable_in_payment_order</c> se algum título da
    /// origem está numa OP e <c>payable_paid</c> se algum já teve pagamento; nesses casos nada muda.
    /// </summary>
    Task<Result<IReadOnlyList<PayableRef>>> ReplacePlannedAsync(DocumentOrigin origin, Guid originId, PlannedPayables plan, CancellationToken cancellationToken = default);

    /// <summary>Cancela todos os títulos em aberto da origem; 409 <c>payable_in_payment_order</c> se algum está numa OP e <c>payable_paid</c> se algum teve pagamento.</summary>
    Task<Result> CancelAllFromOriginAsync(DocumentOrigin origin, Guid originId, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cria os títulos já em aberto (<c>Open</c>, sem a marca "Estimado") por parcela (ADR 0024 §3: os títulos do recebimento), na transação
    /// do chamador; idempotente por parcela (a parcela que já tem título não cancelado não nasce de novo). Conta e natureza vêm do chamador.
    /// </summary>
    Task<Result<IReadOnlyList<PayableRef>>> CreateOpenInstallmentsAsync(DocumentOrigin origin, Guid originId, OpenPayables plan, CancellationToken cancellationToken = default);

    /// <summary>Títulos da origem em todas as parcelas e situações (aba Financeiro do pedido), pela parcela.</summary>
    Task<IReadOnlyList<PayableRef>> ListByOriginAsync(DocumentOrigin origin, Guid originId, CancellationToken cancellationToken = default);

    /// <summary>Títulos a pagar com o projeto (COM-05 CA 6, 8): base do realizado por natureza.</summary>
    Task<IReadOnlyList<ProjectPayable>> ByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);
}

/// <summary>Parcela prevista de uma origem (ADR 0024 §2): número da parcela (1, 2, …), valor, data estimada e a descrição do título.</summary>
public sealed record PlannedInstallment(short Number, decimal Amount, DateOnly EstimatedDueDate, string Description);

/// <summary>
/// Títulos previstos de uma origem por parcela (ADR 0024 §2; CMP-03a CA 12, 15 e 19): fornecedor, projeto, conta do plano e natureza
/// vêm do chamador (o pedido de compra usa os padrões de <c>purchasing.parameters</c>).
/// </summary>
public sealed record PlannedPayables(Guid PartyId, Guid? ProjectId, Guid AccountId, CostType? CostType, IReadOnlyList<PlannedInstallment> Installments);

/// <summary>Parcela em aberto de uma origem (ADR 0024 §3): número da parcela, valor, vencimento e a descrição do título.</summary>
public sealed record OpenInstallment(short Number, decimal Amount, DateOnly DueDate, string Description);

/// <summary>
/// Títulos em aberto de uma origem por parcela (CMP-04 CA 8): fornecedor, projeto, conta do plano e natureza vêm do chamador (o recebimento
/// usa os do pedido).
/// </summary>
public sealed record OpenPayables(Guid PartyId, Guid? ProjectId, Guid AccountId, CostType? CostType, IReadOnlyList<OpenInstallment> Installments);

/// <summary>Título a pagar do projeto (COM-05), sem dado de pagamento.</summary>
public sealed record ProjectPayable(Guid Id, string Number, Guid PartyId, DocumentOrigin Origin, CostType? CostType, DateOnly? DueDate, decimal Amount, decimal Balance, PayableStatus Status, DateTimeOffset CreatedAt);
