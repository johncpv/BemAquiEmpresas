using BemAqui.Sales.Contracts;

namespace BemAqui.Sales.Application;

/// <summary>Estado a que o provedor levou o pagamento (capturado, pendente, falho ou desconhecido).</summary>
public sealed record PaymentProviderOutcome(SalePaymentStatus Status, string? Message = null);

/// <summary>
/// Porta do servidor para os pagamentos (rascunho C §3), com capacidades: o <see cref="ManualPaymentProvider"/> agora; Pix por API
/// (<c>InterPixProvider</c>, PAG-02) e gateway (ECM-03) depois, todos pela mesma máquina de estados.
/// </summary>
public interface IPaymentProvider
{
    PaymentIntegration Integration { get; }

    bool SupportsOffline { get; }

    bool SupportsRefund { get; }

    /// <summary>A confirmação chega depois (Pix, gateway): o pagamento fica <c>Pending</c> até o aviso.</summary>
    bool IsAsync { get; }

    Task<PaymentProviderOutcome> CaptureAsync(OrderPaymentInput payment, CancellationToken cancellationToken = default);
}

/// <summary>
/// Maquininha externa ou dinheiro (rascunho C §4): o operador escolhe a forma e a bandeira e confirma; o pagamento nasce capturado, "atestado
/// pelo operador" (quem confirmou fica no pagamento), e funciona sem rede.
/// </summary>
public sealed class ManualPaymentProvider : IPaymentProvider
{
    public PaymentIntegration Integration => PaymentIntegration.Manual;

    public bool SupportsOffline => true;

    public bool SupportsRefund => false;

    public bool IsAsync => false;

    public Task<PaymentProviderOutcome> CaptureAsync(OrderPaymentInput payment, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PaymentProviderOutcome(SalePaymentStatus.Captured));
}
