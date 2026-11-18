using BemAqui.BuildingBlocks.Security;

namespace BemAqui.BuildingBlocks.Identity;

/// <summary>Resultado da conferência do aparelho de PDV (ADR 0048 §2).</summary>
public sealed record DeviceAuthResult(DeviceAuthStatus Status, DeviceIdentity? Device)
{
    public static readonly DeviceAuthResult Invalid = new(DeviceAuthStatus.Invalid, null);
}

/// <summary>
/// Confere o aparelho de PDV no banco do tenant do host (ADR 0048 §2): o segredo do cookie <c>__Host-bq_pos</c> (atual ou o anterior dentro da
/// tolerância) e a assinatura ECDSA do texto da requisição com a chave pública registrada. Não grava nada: o segredo só gira na sincronização
/// bem-sucedida. Implementado pelo Identity; o esquema <c>Device</c> do Host chama.
/// </summary>
public interface IDeviceAuthenticator
{
    Task<DeviceAuthResult> AuthenticateAsync(string? secret, string signedPayload, string? signature, CancellationToken cancellationToken = default);
}
