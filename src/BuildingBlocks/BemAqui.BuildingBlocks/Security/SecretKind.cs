namespace BemAqui.BuildingBlocks.Security;

/// <summary>
/// Tipo do segredo: entra no propósito do Data Protection, então um ciphertext de um tipo
/// não decifra como outro. Novos tipos (Inter, certificado fiscal) entram na Fase 5 sem mudar o mecanismo.
/// O propósito usa um nome fixo por tipo (<see cref="SecretProtector.Purposes"/>), não o nome do enum.
/// </summary>
public enum SecretKind
{
    TenantConnection,
    SmtpPassword,
    TotpSecret,
    AiApiKey,

    /// <summary>Segredo de assinatura de webhook por provedor e tenant (FND-16, ADR 0007 adendo de 21/10/2026).</summary>
    WebhookSecret,
}
