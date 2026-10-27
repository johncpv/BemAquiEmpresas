using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using BemAqui.Finance.Infrastructure.Inter;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>Certificado de teste gerado na hora (nenhum arquivo no repositório): PEM do certificado e da chave, e o .pfx.</summary>
public sealed record TestInterCertificate(string CertificatePem, string KeyPem, byte[] Pfx, string PfxPassword, string Thumbprint, DateTimeOffset NotAfter)
{
    public byte[] CertificateBytes => Encoding.ASCII.GetBytes(CertificatePem);

    public byte[] KeyBytes => Encoding.ASCII.GetBytes(KeyPem);

    public static TestInterCertificate Create(string subject = "CN=Empresa A Ltda:47225005000186, O=Banco Inter", DateTimeOffset? notBefore = null, DateTimeOffset? notAfter = null, int bits = 2048)
    {
        using var rsa = RSA.Create(bits);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var from = notBefore ?? DateTimeOffset.UtcNow.AddDays(-1);
        var to = notAfter ?? DateTimeOffset.UtcNow.AddYears(1);
        using var certificate = request.CreateSelfSigned(from, to);
        const string password = "senha-do-pfx";
        return new TestInterCertificate(certificate.ExportCertificatePem(), rsa.ExportPkcs8PrivateKeyPem(), certificate.Export(X509ContentType.Pfx, password), password,
            certificate.GetCertHashString(HashAlgorithmName.SHA256).ToLowerInvariant(), new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero));
    }
}

/// <summary>Uma chamada recebida pelo fake do Inter (para conferir caminho, host, cabeçalhos, formulário e o certificado do mTLS).</summary>
public sealed record FakeInterCall(string Host, string Method, string Path, string? Authorization, string? AccountHeader, IReadOnlyDictionary<string, string> Form, string CertificateThumbprint);

/// <summary>Comportamento do fake por client id.</summary>
public sealed class FakeInterAccount
{
    /// <summary>Escopos que o Inter concede (o fake devolve a interseção com os pedidos; os de pagamento só se estiverem aqui).</summary>
    public string Granted { get; set; } = "boleto-cobranca.read boleto-cobranca.write extrato.read webhook.read webhook.write";

    /// <summary>Recusa o pedido com escopo não concedido com 400 (em vez de devolver só a interseção).</summary>
    public bool RejectUnknownScopes { get; set; }

    public string ClientSecret { get; set; } = "segredo-de-teste";

    public int ExpiresIn { get; set; } = 3600;

    public HttpStatusCode BalanceStatus { get; set; } = HttpStatusCode.OK;

    /// <summary>Responde 401 à próxima chamada da API (não ao token), como um token revogado antes da hora.</summary>
    public int UnauthorizedOnce { get; set; }

    public int TokenDelayMs { get; set; }

    /// <summary>Só aceita este certificado no mTLS (thumbprint); nulo aceita qualquer.</summary>
    public string? RequiredThumbprint { get; set; }
}

/// <summary>
/// Fake do Inter com "mTLS de teste": a fábrica recebe o certificado do cliente (como o <see cref="SocketsInterHandlerFactory"/>) e o
/// handler responde o OAuth2 e o saldo, gravando cada chamada. Conta handlers criados e descartados (CA 9).
/// </summary>
public sealed class FakeInter : IInterHandlerFactory
{
    public ConcurrentDictionary<string, FakeInterAccount> Accounts { get; } = new(StringComparer.Ordinal);

    public ConcurrentQueue<FakeInterCall> Calls { get; } = new();

    public ConcurrentQueue<string> CreatedFor { get; } = new();

    public int Disposed => _disposed;

    private int _disposed;
    private int _tokens;

    public int TokenRequests => _tokens;

    public FakeInterAccount Account(string clientId) => Accounts.GetOrAdd(clientId, _ => new FakeInterAccount());

    public HttpMessageHandler Create(X509Certificate2 clientCertificate)
    {
        ArgumentNullException.ThrowIfNull(clientCertificate);
        var thumbprint = clientCertificate.GetCertHashString(HashAlgorithmName.SHA256).ToLowerInvariant();
        clientCertificate.HasPrivateKey.Should().BeTrue("o mTLS precisa da chave privada em memória");
        CreatedFor.Enqueue(thumbprint);
        return new InterHostAllowListHandler(new Handler(this, thumbprint));
    }

    public IEnumerable<FakeInterCall> CallsTo(string path) => Calls.Where(c => c.Path == "/" + path);

    private sealed class Handler(FakeInter fake, string thumbprint) : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<string, string> _issued = new(StringComparer.Ordinal);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var form = request.Content is FormUrlEncodedContent content
                ? (await content.ReadAsStringAsync(cancellationToken)).Split('&').Select(p => p.Split('=')).ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1].Replace('+', ' ')))
                : new Dictionary<string, string>();
            fake.Calls.Enqueue(new FakeInterCall(request.RequestUri!.Host, request.Method.Method, request.RequestUri.AbsolutePath, request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues(InterEnvironments.AccountHeader, out var account) ? account.First() : null, form, thumbprint));

            if (request.RequestUri.AbsolutePath == "/" + InterEnvironments.TokenPath)
            {
                Interlocked.Increment(ref fake._tokens);
                if (!fake.Accounts.TryGetValue(form.GetValueOrDefault("client_id") ?? string.Empty, out var settings) || settings.ClientSecret != form.GetValueOrDefault("client_secret")
                    || (settings.RequiredThumbprint is { } required && required != thumbprint))
                {
                    return Json(HttpStatusCode.Unauthorized, "{\"title\":\"invalid_client\"}");
                }

                if (settings.TokenDelayMs > 0)
                {
                    await Task.Delay(settings.TokenDelayMs, cancellationToken);
                }

                var requested = (form.GetValueOrDefault("scope") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var granted = settings.Granted.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (settings.RejectUnknownScopes && requested.Except(granted).Any())
                {
                    return Json(HttpStatusCode.BadRequest, "{\"title\":\"invalid_scope\"}");
                }

                var token = "tok-" + Guid.NewGuid().ToString("N");
                _issued[token] = form["client_id"];
                var scope = string.Join(' ', requested.Intersect(granted));
                return Json(HttpStatusCode.OK, $"{{\"access_token\":\"{token}\",\"token_type\":\"Bearer\",\"expires_in\":{settings.ExpiresIn},\"scope\":\"{scope}\"}}");
            }

            var bearer = request.Headers.Authorization?.Parameter;
            if (bearer is null || !_issued.TryGetValue(bearer, out var clientId) || !fake.Accounts.TryGetValue(clientId, out var owner))
            {
                return Json(HttpStatusCode.Unauthorized, "{}");
            }

            if (owner.UnauthorizedOnce > 0)
            {
                owner.UnauthorizedOnce--;
                return Json(HttpStatusCode.Unauthorized, "{}");
            }

            var response = Json(owner.BalanceStatus, "{\"disponivel\":1500.25,\"pagador\":{\"nome\":\"Maria da Silva\",\"cpf\":\"52998224725\"}}");
            response.Headers.Add("x-correlation-id", "corr-" + Guid.NewGuid().ToString("N")[..8]);
            return response;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Interlocked.Increment(ref fake._disposed);
            }

            base.Dispose(disposing);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
