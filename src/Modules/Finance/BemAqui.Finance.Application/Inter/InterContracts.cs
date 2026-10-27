using System.Security.Cryptography.X509Certificates;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Application.Inter;

/// <summary>
/// Credencial do Inter em memória, pronta para o mTLS (BNK-01a CA 9): client id, client secret e como abrir o certificado com a chave
/// (<c>EphemeralKeySet</c>, nunca em disco). O handler em cache abre a própria cópia do certificado (<see cref="LoadCertificate"/>) e a
/// descarta junto com ele. Vive só durante a chamada.
/// </summary>
public sealed class InterCredentials(Guid tenantId, Guid integrationId, BankEnvironment environment, string clientId, Secret clientSecret, Func<X509Certificate2> loadCertificate, string thumbprint, string? accountNumber)
    : IDisposable
{
    private Func<X509Certificate2>? _load = loadCertificate;

    public Guid TenantId { get; } = tenantId;

    public Guid IntegrationId { get; } = integrationId;

    public BankEnvironment Environment { get; } = environment;

    public string ClientId { get; } = clientId;

    public Secret ClientSecret { get; } = clientSecret;

    /// <summary>Abre uma cópia nova do certificado com a chave, só em memória; quem recebe descarta.</summary>
    public X509Certificate2 LoadCertificate() => (_load ?? throw new ObjectDisposedException(nameof(InterCredentials)))();

    /// <summary>SHA-256 do certificado (hex), parte da chave do handler e do token em cache.</summary>
    public string Thumbprint { get; } = thumbprint;

    public string? AccountNumber { get; } = accountNumber;

    /// <summary>Chave do cache do handler e do token (CA 9 e 10): <c>{tenant}:{integração}:{thumbprint}</c>.</summary>
    public string CacheKey => $"{TenantId:N}:{IntegrationId:N}:{Thumbprint}";

    public override string ToString() => $"InterCredentials({TenantId:N}:{IntegrationId:N})";

    public void Dispose() => _load = null;
}

/// <summary>Recusa do Inter ou da rede, já em PT-BR com o que fazer (padrão 6: "Recusado com motivo e ação").</summary>
public enum InterFailureKind
{
    /// <summary>Client id, client secret ou certificado recusados no token (400/401 do OAuth).</summary>
    CredentialsRefused,

    /// <summary>Token sem um escopo necessário, ou chamada com 403.</summary>
    Forbidden,

    /// <summary>Falha de rede, TLS ou tempo esgotado.</summary>
    Unreachable,

    /// <summary>Erro do lado do Inter (5xx) ou resposta fora do esperado.</summary>
    BankError,
}

public sealed record InterFailure(InterFailureKind Kind, string Message, string Action, int? HttpStatus = null);

/// <summary>Token obtido: escopos concedidos (o que o Inter devolveu em <c>scope</c>) e vencimento.</summary>
public sealed record InterTokenInfo(IReadOnlyList<string> GrantedScopes, DateTimeOffset ExpiresAt);

/// <summary>Resultado do "Testar conexão" (CA 7): token, escopos e a leitura sem efeito (saldo).</summary>
public sealed record InterConnectionCheck(IReadOnlyList<string> GrantedScopes, bool BalanceRead);

/// <summary>Resultado de uma chamada ao Inter: o valor ou a recusa com motivo.</summary>
public sealed record InterResult<T>(T? Value, InterFailure? Failure)
{
    public bool Ok => Failure is null;

    public static InterResult<T> Success(T value) => new(value, null);

    public static InterResult<T> Fail(InterFailure failure) => new(default, failure);
}

/// <summary>
/// Cliente da API do Inter (Finance.Infrastructure, ADR 0032 §2 e §3): OAuth2 <c>client_credentials</c> com mTLS, um handler por
/// conta em cache pelo thumbprint, token em cache até 60 s antes de vencer, URL base fixa por ambiente e log sem segredo.
/// </summary>
public interface IInterApiClient
{
    /// <summary>
    /// Pede um token novo com os escopos de prova (sem usar nem gravar o cache), para conferir uma credencial nova antes de gravar
    /// ou trocar (CA 8 e 14). Devolve os escopos concedidos.
    /// </summary>
    Task<InterResult<InterTokenInfo>> ProbeAsync(InterCredentials credentials, CancellationToken cancellationToken = default);

    /// <summary>"Testar conexão" (CA 7): token (com a prova de escopos) e a leitura do saldo, sem efeito no banco.</summary>
    Task<InterResult<InterConnectionCheck>> TestConnectionAsync(InterCredentials credentials, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chamada à API com o token em cache (CA 10): renova sob lock até 60 s antes de vencer e, num 401, renova uma vez e repete. Base
    /// das operações da BNK-01b (boleto), BNK-02 (reconsulta) e BNK-03a (extrato). O <paramref name="request"/> traz só o caminho
    /// relativo: a URL base é a fixa do ambiente.
    /// </summary>
    Task<InterResult<InterResponse>> SendAsync(InterCredentials credentials, InterRequest request, CancellationToken cancellationToken = default);

    /// <summary>Descarta o handler e o token em cache da integração (troca ou desligamento, CA 14 e 16).</summary>
    void Forget(Guid tenantId, Guid integrationId);
}

/// <param name="Operation">Nome curto para o log (ex.: <c>saldo</c>, <c>boleto.emitir</c>); nunca dado do pagador.</param>
/// <param name="Path">Caminho relativo à URL base fixa do ambiente (ex.: <c>banking/v2/saldo</c>).</param>
public sealed record InterRequest(string Operation, HttpMethod Method, string Path, Func<HttpContent>? Content = null);

/// <summary>Resposta do Inter (situação e corpo). O corpo não vai para o log.</summary>
public sealed record InterResponse(int Status, string Body)
{
    public override string ToString() => $"InterResponse({Status})";
}
