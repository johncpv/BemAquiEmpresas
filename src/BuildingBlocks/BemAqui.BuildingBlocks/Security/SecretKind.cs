namespace BemAqui.BuildingBlocks.Security;

/// <summary>
/// Tipo do segredo: entra no propósito do Data Protection, então um ciphertext de um tipo
/// não decifra como outro. Novos tipos (Inter, certificado fiscal) entram na Fase 5 sem mudar o mecanismo.
/// Nunca renomeie um valor existente: o nome faz parte do propósito.
/// </summary>
public enum SecretKind
{
    TenantConnection,
    SmtpPassword,
    TotpSecret,
    AiApiKey,
}
