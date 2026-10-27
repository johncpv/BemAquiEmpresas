using BemAqui.SharedKernel;

namespace BemAqui.Fiscal.Domain;

/// <summary>Ambiente da SEFAZ em que o emitente do EmissorFiscal está (lido do <c>GET /me</c>, FIS-01 CA 8 e 9).</summary>
public enum FiscalEnvironment
{
    Homologation,
    Production,
}

/// <summary>Situação de um documento no EmissorFiscal, com os mesmos significados de lá (ADR 0027; guia de integração §5).</summary>
public enum FiscalDocumentStatus
{
    Received,
    Processing,
    Authorized,
    Rejected,
    Cancelled,
    Denied,
    ContingencyPending,
}

/// <summary>
/// Integração com o EmissorFiscal do tenant (<c>fiscal.integration_settings</c>, FIS-01 CA 7 a 10, ADR 0027 §2): uma linha por tenant
/// (<see cref="Slot"/> único). A chave de API fica cifrada (<c>fiscal-api-key</c>) e nunca volta pela API; o segredo do webhook fica na
/// credencial do provedor <c>emissor-fiscal</c> da FND-16 (<c>shared.webhook_tokens</c>, com a sobreposição de 24 h). Emitente, ambiente,
/// escopos e certificado vêm do último "Testar conexão" aprovado. Auditada sem os segredos.
/// </summary>
[DocumentName("A integração fiscal")]
public sealed class FiscalIntegrationSettings : Entity, IAuditable, IConcurrencyVersioned
{
    public const int SingleSlot = 1;
    public const int BaseUrlMaxLength = 200;
    public const int MessageMaxLength = 300;

    public int Slot { get; init; } = SingleSlot;

    public required string BaseUrl { get; set; }

    [ProtectedSecret]
    public string? ApiKeyCipher { get; private set; }

    public DateTimeOffset? ApiKeySetAt { get; private set; }

    /// <summary>Ligada só depois de um teste aprovado (CA 8); sem ela, nada fiscal sai.</summary>
    public bool Active { get; private set; }

    public Guid? EmitterClientId { get; private set; }

    /// <summary>Tenant do Emissor da chave: o webhook de outro tenant é ignorado (CA 11).</summary>
    public Guid? EmitterTenantId { get; private set; }

    public string? IssuerDocument { get; private set; }

    public string? IssuerName { get; private set; }

    public FiscalEnvironment? Environment { get; private set; }

    /// <summary>Escopos efetivos da chave, separados por vírgula (ex.: <c>consultar,nfe:emitir,nfe:eventos</c>).</summary>
    public string? Scopes { get; private set; }

    public DateTimeOffset? CertificateValidUntil { get; private set; }

    public DateTimeOffset? LastTestAt { get; private set; }

    /// <summary>Código do último teste (<see cref="FiscalTestResults"/>).</summary>
    public string? LastTestResult { get; private set; }

    public string? LastTestMessage { get; private set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public uint Version { get; set; }

    public IReadOnlyList<string> ScopeList => string.IsNullOrEmpty(Scopes) ? [] : Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Chave nova (primeira ou rotação, CA 10): a antiga deixa de ser usada na mesma gravação.</summary>
    public void SetApiKey(string cipher, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrEmpty(cipher);
        ApiKeyCipher = cipher;
        ApiKeySetAt = at;
    }

    /// <summary>Teste aprovado: guarda o que o <c>GET /me</c> disse e liga a integração.</summary>
    public void RecordApprovedTest(FiscalAccountSnapshot account, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(account);
        EmitterClientId = account.ClientId;
        EmitterTenantId = account.TenantId;
        IssuerDocument = account.IssuerDocument;
        IssuerName = Truncate(account.IssuerName, 200);
        Environment = account.Environment;
        Scopes = string.Join(',', account.Scopes.Order(StringComparer.Ordinal));
        CertificateValidUntil = account.CertificateValidUntil;
        Active = true;
        LastTestAt = at;
        LastTestResult = FiscalTestResults.Ok;
        LastTestMessage = null;
    }

    /// <summary>
    /// Teste recusado (CA 8 e 9): a integração fica desligada quando a chave está errada (escopo, emitente, ambiente ou recusada); falha
    /// de rede não muda a situação (<paramref name="deactivate"/> falso).
    /// </summary>
    public void RecordFailedTest(string result, string message, bool deactivate, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrEmpty(result);
        LastTestAt = at;
        LastTestResult = result;
        LastTestMessage = Truncate(message, MessageMaxLength);
        if (deactivate)
        {
            Active = false;
        }
    }

    /// <summary>URL trocada: a integração só volta depois de um teste aprovado.</summary>
    public void Deactivate() => Active = false;

    private static string? Truncate(string? value, int max) => value is { Length: > 0 } && value.Length > max ? value[..max] : value;
}

/// <summary>O que o teste de conexão confirmou do <c>GET /api/v1/me</c> do Emissor.</summary>
public sealed record FiscalAccountSnapshot(Guid ClientId, Guid TenantId, string IssuerDocument, string IssuerName, FiscalEnvironment Environment, IReadOnlyList<string> Scopes, DateTimeOffset? CertificateValidUntil);

/// <summary>Resultado do "Testar conexão" (CA 8 e 9), gravado na integração.</summary>
public static class FiscalTestResults
{
    public const string Ok = "ok";
    public const string AdminScope = "admin_scope";
    public const string MissingScopes = "missing_scopes";
    public const string IssuerMismatch = "issuer_mismatch";
    public const string EnvironmentNotAllowed = "environment_not_allowed";
    public const string KeyRejected = "key_rejected";
    public const string Unavailable = "unavailable";
}

/// <summary>Regras da chave do Emissor (ADR 0027 §2, CA 8).</summary>
public static class FiscalScopes
{
    public const string Admin = "admin";
    public const string IssueNfe = "nfe:emitir";
    public const string NfeEvents = "nfe:eventos";
    public const string Read = "consultar";

    /// <summary>Escopos exigidos nesta fase; o <c>nfse:emitir</c> entra na FIS-06.</summary>
    public static readonly string[] Required = [IssueNfe, NfeEvents, Read];

    public static IReadOnlyList<string> Missing(IEnumerable<string> scopes)
    {
        var have = scopes.ToHashSet(StringComparer.Ordinal);
        return Required.Where(s => !have.Contains(s)).ToList();
    }

    public static bool HasAdmin(IEnumerable<string> scopes) => scopes.Contains(Admin, StringComparer.Ordinal);
}
