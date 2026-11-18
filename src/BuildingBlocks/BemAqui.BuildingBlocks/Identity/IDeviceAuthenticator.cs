using BemAqui.BuildingBlocks.Security;

namespace BemAqui.BuildingBlocks.Identity;

/// <summary>Resultado da conferência do aparelho de PDV (ADR 0048 §2).</summary>
public sealed record DeviceAuthResult(DeviceAuthStatus Status, DeviceIdentity? Device)
{
    public static readonly DeviceAuthResult Invalid = new(DeviceAuthStatus.Invalid, null);
}

/// <summary>
/// Confere o aparelho de PDV no banco do tenant do host (ADR 0048 §2). Primeiro acha o aparelho pelo segredo do cookie <c>__Host-bq_pos</c> (sem
/// aparelho, nada do corpo é lido); só então pede o texto assinado (<c>signedPayload</c>, que lê o corpo) e confere a assinatura ECDSA com a chave
/// pública registrada. O segredo anterior vale até o atual ser usado (ou uma vez, para a resposta perdida); apresentado depois disso, é reuso: o
/// aparelho é revogado e o Proprietário avisado. Implementado pelo Identity; o esquema <c>Device</c> do Host chama.
/// </summary>
public interface IDeviceAuthenticator
{
    Task<DeviceAuthResult> AuthenticateAsync(string? secret, Func<CancellationToken, Task<string>> signedPayload, string? signature, CancellationToken cancellationToken = default);
}
