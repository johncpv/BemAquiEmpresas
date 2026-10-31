using BemAqui.SharedKernel;

namespace BemAqui.Finance.Domain;

/// <summary>Banco ligado por API à conta (<c>finance.bank_accounts.provider</c>, BNK-01a, ADR 0032 §1). Sem API, <c>None</c>.</summary>
public enum BankProvider
{
    None,
    Inter,
}

/// <summary>Ambiente do banco: Sandbox (testes no tenant <c>demo</c>) ou Produção (cobrança real, só depois da GOL-01b).</summary>
public enum BankEnvironment
{
    Sandbox,
    Production,
}

/// <summary>Formato do certificado do mTLS enviado: <c>.crt</c> + <c>.key</c> (PEM) ou <c>.pfx</c> com senha (BNK-01a CA 4).</summary>
public enum BankCertificateFormat
{
    Pem,
    Pfx,
}

public enum BankIntegrationStatus
{
    Active,
    Disabled,
}

/// <summary>Resultado do último "Testar conexão" (CA 7 e 8).</summary>
public enum BankTestOutcome
{
    Ok,
    MissingScopes,
    PaymentScopes,
    Refused,
}

/// <summary>
/// Integração bancária de uma conta (<c>finance.bank_integrations</c>, BNK-01a, ADR 0032 §1): uma por conta. Client id, client
/// secret, certificado, chave privada e senha do <c>.pfx</c> ficam cifrados por tenant e por integração (<c>inter-credentials</c> e
/// <c>inter-certificate</c>, ADR 0007 adendo) e são só de escrita: nunca voltam na API, no log nem na auditoria
/// (<see cref="ProtectedSecretAttribute"/>). Desligar sobrescreve os segredos (nulos) e a conta volta a <see cref="BankProvider.None"/>.
/// </summary>
[DocumentName("Integração bancária")]
public sealed class BankIntegration : Entity, IAuditable, IConcurrencyVersioned
{
    public Guid BankAccountId { get; init; }

    public BankProvider Provider { get; set; } = BankProvider.Inter;

    public BankEnvironment Environment { get; set; }

    public BankIntegrationStatus Status { get; private set; } = BankIntegrationStatus.Active;

    [ProtectedSecret]
    public string? ClientIdCipher { get; private set; }

    /// <summary>Últimos 4 caracteres do client id, para a tela mostrar "•••• 1a2b" sem decifrar (CA 5).</summary>
    public string? ClientIdHint { get; private set; }

    [ProtectedSecret]
    public string? ClientSecretCipher { get; private set; }

    [ProtectedSecret]
    public string? CertificateCipher { get; private set; }

    [ProtectedSecret]
    public string? PrivateKeyCipher { get; private set; }

    [ProtectedSecret]
    public string? CertificatePasswordCipher { get; private set; }

    public BankCertificateFormat? CertificateFormat { get; private set; }

    public DateTimeOffset? CertificateNotAfter { get; private set; }

    /// <summary>SHA-256 do certificado (hex): chave do handler mTLS em cache (CA 9) e conferência na tela.</summary>
    public string? CertificateThumbprint { get; private set; }

    /// <summary>Titular (assunto) do certificado, sem a chave.</summary>
    public string? CertificateSubject { get; private set; }

    /// <summary>Conta corrente no Inter (cabeçalho <c>x-conta-corrente</c>), sem dígito separado.</summary>
    public string? AccountNumber { get; set; }

    /// <summary>Escopos concedidos pelo Inter no último token (separados por espaço).</summary>
    public string? GrantedScopes { get; private set; }

    public DateTimeOffset ConfiguredAt { get; private set; }

    public Guid? ConfiguredBy { get; private set; }

    public DateTimeOffset? RotatedAt { get; private set; }

    public Guid? RotatedBy { get; private set; }

    public DateTimeOffset? LastTestAt { get; private set; }

    public BankTestOutcome? LastTestOutcome { get; private set; }

    /// <summary>Motivo do último teste recusado, em PT-BR, sem nenhum segredo.</summary>
    public string? LastTestMessage { get; private set; }

    public DateTimeOffset? DisabledAt { get; private set; }

    public Guid? DisabledBy { get; private set; }

    /// <summary>Aviso de pagamento cadastrado no Inter (BNK-02 CA 1): desde quando e por quem.</summary>
    public DateTimeOffset? WebhookRegisteredAt { get; private set; }

    public Guid? WebhookRegisteredBy { get; private set; }

    /// <summary>Última troca do endereço do aviso (CA 3).</summary>
    public DateTimeOffset? WebhookRotatedAt { get; private set; }

    /// <summary>Onde vai o token do aviso: <c>Query</c> (<c>?t=</c>) ou <c>PathToken</c> (último segmento), CA 2.</summary>
    public string? WebhookMode { get; private set; }

    /// <summary>Última conferência diária com sucesso (CA 11): a próxima consulta começa 2 dias antes.</summary>
    public DateTimeOffset? LastReconcileAt { get; private set; }

    /// <summary>Relatório da última conferência (JSONB: consultados, baixados, divergentes, sem dado do pagador).</summary>
    public string? LastReconcileReport { get; private set; }

    /// <summary>Execuções seguidas da conferência sem sucesso (CA 12: na 3ª, alerta e aviso).</summary>
    public int ReconcileFailures { get; private set; }

    /// <summary>Motivo da última falha da conferência, em PT-BR, sem segredo.</summary>
    public string? LastReconcileError { get; private set; }

    public uint Version { get; set; }

    public bool WebhookActive => IsActive && WebhookRegisteredAt is not null;

    /// <summary>Aviso cadastrado no Inter (ativar ou trocar o endereço).</summary>
    public void RegisterWebhook(string mode, Guid? by, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);
        if (WebhookRegisteredAt is null)
        {
            WebhookRegisteredAt = at;
            WebhookRegisteredBy = by;
        }
        else
        {
            WebhookRotatedAt = at;
        }

        WebhookMode = mode;
    }

    public void RecordReconcile(DateTimeOffset at, string report)
    {
        LastReconcileAt = at;
        LastReconcileReport = report;
        ReconcileFailures = 0;
        LastReconcileError = null;
    }

    /// <summary>Conferência sem sucesso; devolve quantas seguidas.</summary>
    public int RecordReconcileFailure(string message)
    {
        ReconcileFailures++;
        LastReconcileError = message is { Length: > 300 } ? message[..300] : message;
        return ReconcileFailures;
    }

    public bool IsActive => Status == BankIntegrationStatus.Active;

    /// <summary>Certificado vencido no instante dado (a emissão responde 409 <c>bank_integration_expired</c>, CA 15).</summary>
    public bool IsExpired(DateTimeOffset now) => CertificateNotAfter is { } notAfter && notAfter <= now;

    public static BankIntegration For(Guid bankAccountId) => new() { BankAccountId = bankAccountId };

    /// <summary>Grava (primeira configuração ou religar) as credenciais já cifradas e o retrato do certificado.</summary>
    public void Configure(BankIntegrationSecrets secrets, BankCertificateInfo certificate, string? grantedScopes, Guid? by, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(certificate);
        Apply(secrets, certificate, grantedScopes);
        Status = BankIntegrationStatus.Active;
        ConfiguredAt = at;
        ConfiguredBy = by;
        RotatedAt = null;
        RotatedBy = null;
        DisabledAt = null;
        DisabledBy = null;
        RecordTest(at, BankTestOutcome.Ok, null, grantedScopes);
    }

    /// <summary>Troca as credenciais (CA 14) depois de a nova ter passado no teste: mesma linha, data da troca.</summary>
    public void Rotate(BankIntegrationSecrets secrets, BankCertificateInfo certificate, string? grantedScopes, Guid? by, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(certificate);
        Apply(secrets, certificate, grantedScopes);
        RotatedAt = at;
        RotatedBy = by;
        RecordTest(at, BankTestOutcome.Ok, null, grantedScopes);
    }

    public void RecordTest(DateTimeOffset at, BankTestOutcome outcome, string? message, string? grantedScopes)
    {
        LastTestAt = at;
        LastTestOutcome = outcome;
        LastTestMessage = message is { Length: > 300 } ? message[..300] : message;
        if (grantedScopes is not null)
        {
            GrantedScopes = grantedScopes;
        }
    }

    /// <summary>Desligar (CA 16): os segredos são sobrescritos (nulos) e o retrato do certificado sai; o histórico continua.</summary>
    public void Disable(Guid? by, DateTimeOffset at)
    {
        ClientIdCipher = null;
        ClientSecretCipher = null;
        CertificateCipher = null;
        PrivateKeyCipher = null;
        CertificatePasswordCipher = null;
        ClientIdHint = null;
        CertificateFormat = null;
        CertificateNotAfter = null;
        CertificateThumbprint = null;
        CertificateSubject = null;
        GrantedScopes = null;
        WebhookRegisteredAt = null;
        WebhookRegisteredBy = null;
        WebhookRotatedAt = null;
        WebhookMode = null;
        Status = BankIntegrationStatus.Disabled;
        DisabledAt = at;
        DisabledBy = by;
    }

    private void Apply(BankIntegrationSecrets secrets, BankCertificateInfo certificate, string? grantedScopes)
    {
        ClientIdCipher = secrets.ClientIdCipher;
        ClientIdHint = secrets.ClientIdHint;
        ClientSecretCipher = secrets.ClientSecretCipher;
        CertificateCipher = secrets.CertificateCipher;
        PrivateKeyCipher = secrets.PrivateKeyCipher;
        CertificatePasswordCipher = secrets.CertificatePasswordCipher;
        CertificateFormat = certificate.Format;
        CertificateNotAfter = certificate.NotAfter;
        CertificateThumbprint = certificate.Thumbprint;
        CertificateSubject = certificate.Subject is { Length: > 300 } subject ? subject[..300] : certificate.Subject;
        GrantedScopes = grantedScopes;
    }
}

/// <summary>Segredos já cifrados (o domínio nunca vê o valor em claro).</summary>
public sealed record BankIntegrationSecrets(
    string ClientIdCipher, string ClientIdHint, string ClientSecretCipher, string CertificateCipher, string? PrivateKeyCipher, string? CertificatePasswordCipher);

/// <summary>Retrato público do certificado: formato, validade, impressão digital (SHA-256) e titular.</summary>
public sealed record BankCertificateInfo(BankCertificateFormat Format, DateTimeOffset NotAfter, string Thumbprint, string Subject);
