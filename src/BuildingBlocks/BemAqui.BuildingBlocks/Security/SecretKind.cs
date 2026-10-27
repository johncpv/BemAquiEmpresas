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

    /// <summary>Client id e client secret do Banco Inter, por integração de conta (BNK-01a, ADR 0007 adendo de 21/10/2026).</summary>
    InterCredentials,

    /// <summary>Certificado, chave privada e senha do <c>.pfx</c> do mTLS do Inter, por integração de conta (BNK-01a).</summary>
    InterCertificate,
    /// <summary>Chave de API do EmissorFiscal por tenant, sem o escopo <c>admin</c> (FIS-01, ADR 0027 §2, ADR 0007 adendo de 21/10/2026).</summary>
    FiscalApiKey,
}
