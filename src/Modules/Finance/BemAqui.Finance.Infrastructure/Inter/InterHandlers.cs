using System.Collections.Concurrent;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using BemAqui.Finance.Application.Inter;
using BemAqui.Finance.Domain;
using Microsoft.Extensions.Options;

namespace BemAqui.Finance.Infrastructure.Inter;

/// <summary>
/// URL base fixa por ambiente, no código (BNK-01a CA 9, ADR 0032 §3): nenhuma URL vem da tela nem do banco (contra SSRF). Os
/// caminhos são os da documentação pública do Inter, conferidos no sandbox (runbook <c>docs/operacao/banco-inter.md</c>).
/// </summary>
public static class InterEnvironments
{
    public static readonly Uri Sandbox = new("https://cdpj-sandbox.partners.uatinter.co/");
    public static readonly Uri Production = new("https://cdpj.partners.bancointer.com.br/");

    public const string TokenPath = "oauth/v2/token";
    public const string BalancePath = "banking/v2/saldo";

    /// <summary>API de Cobrança v3 (bolepix, BNK-01b): emitir, consultar, PDF e cancelar (caminhos conferidos no sandbox).</summary>
    public const string BillingPath = "cobranca/v3/cobrancas";
    public const string AccountHeader = "x-conta-corrente";

    /// <summary>Únicos hosts que o cliente do Inter alcança (handler de allowlist; a regra de saída do container fica no runbook).</summary>
    public static readonly IReadOnlySet<string> Hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Sandbox.Host, Production.Host };

    public static Uri BaseUrl(BankEnvironment environment) => environment switch
    {
        BankEnvironment.Sandbox => Sandbox,
        BankEnvironment.Production => Production,
        _ => throw new ArgumentOutOfRangeException(nameof(environment), environment, "Ambiente do Inter desconhecido."),
    };
}

/// <summary>Recusa qualquer requisição fora dos hosts do Inter e fora de HTTPS (ADR 0032 §3), antes de abrir conexão.</summary>
public sealed class InterHostAllowListHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestUri is not { Scheme: "https" } uri || !InterEnvironments.Hosts.Contains(uri.Host))
        {
            throw new InvalidOperationException("Saída para fora dos hosts do Inter recusada.");
        }

        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>Monta o handler HTTP de uma conta com o certificado do mTLS. Nos testes, um fake do Inter entra no lugar.</summary>
public interface IInterHandlerFactory
{
    HttpMessageHandler Create(X509Certificate2 clientCertificate);
}

/// <summary>
/// <see cref="SocketsHttpHandler"/> com o certificado do cliente só em memória (ADR 0032 §2): TLS 1.2+, validação padrão do certificado
/// do servidor (nenhum callback permissivo; teste de arquitetura), sem proxy implícito, sem redirecionamento e sem cookies, com a vida das
/// conexões limitada. Por fora, a allowlist dos hosts do Inter.
/// </summary>
public sealed class SocketsInterHandlerFactory(IOptions<InterOptions> options) : IInterHandlerFactory
{
    public HttpMessageHandler Create(X509Certificate2 clientCertificate)
    {
        ArgumentNullException.ThrowIfNull(clientCertificate);
        var sockets = new SocketsHttpHandler
        {
            PooledConnectionLifetime = options.Value.ConnectionLifetime,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            AllowAutoRedirect = false,
            UseCookies = false,
            SslOptions = new SslClientAuthenticationOptions
            {
                ClientCertificates = new X509CertificateCollection { clientCertificate },
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            },
        };
        return new InterHostAllowListHandler(sockets);
    }
}

/// <summary>
/// Um handler por conta (BNK-01a CA 9, ADR 0032 §2), em cache pela chave <c>{tenant}:{integração}:{thumbprint}</c>: o certificado
/// é aberto só em memória (<c>EphemeralKeySet</c>) e descartado com o handler. Na troca ou no desligamento, o handler sai do cache e
/// é descartado depois de <see cref="InterOptions.HandlerDisposeDelay"/>, para as chamadas em curso terminarem. Nunca mistura contas.
/// </summary>
public sealed class InterHandlerCache(IInterHandlerFactory factory, IOptions<InterOptions> options) : IDisposable
{
    private sealed record Entry(HttpMessageInvoker Invoker, HttpMessageHandler Handler, X509Certificate2 Certificate)
    {
        public void Dispose()
        {
            Invoker.Dispose();
            Handler.Dispose();
            Certificate.Dispose();
        }
    }

    private readonly ConcurrentDictionary<string, Lazy<Entry>> _entries = new(StringComparer.Ordinal);

    /// <summary>Chaves em cache (para os testes de isolamento entre contas e tenants).</summary>
    public IReadOnlyCollection<string> Keys => _entries.Keys.ToList();

    public HttpMessageInvoker Get(InterCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        return _entries.GetOrAdd(credentials.CacheKey, _ => new Lazy<Entry>(() =>
        {
            var certificate = credentials.LoadCertificate();
            var handler = factory.Create(certificate);
            return new Entry(new HttpMessageInvoker(handler, disposeHandler: false), handler, certificate);
        })).Value.Invoker;
    }

    /// <summary>Tira do cache todos os handlers da integração (qualquer thumbprint) e agenda o descarte.</summary>
    public int Forget(Guid tenantId, Guid integrationId)
    {
        var prefix = $"{tenantId:N}:{integrationId:N}:";
        var removed = 0;
        foreach (var key in _entries.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            if (_entries.TryRemove(key, out var entry) && entry.IsValueCreated)
            {
                Retire(entry.Value);
                removed++;
            }
        }

        return removed;
    }

    private void Retire(Entry entry)
    {
        var delay = options.Value.HandlerDisposeDelay;
        if (delay <= TimeSpan.Zero)
        {
            entry.Dispose();
            return;
        }

        _ = Task.Delay(delay).ContinueWith(_ => entry.Dispose(), TaskScheduler.Default);
    }

    public void Dispose()
    {
        foreach (var entry in _entries.Values.Where(e => e.IsValueCreated))
        {
            entry.Value.Dispose();
        }

        _entries.Clear();
    }
}
