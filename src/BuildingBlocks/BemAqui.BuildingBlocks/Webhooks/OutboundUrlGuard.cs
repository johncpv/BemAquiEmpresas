using System.Net;
using System.Net.Sockets;

namespace BemAqui.BuildingBlocks.Webhooks;

/// <summary>Seção <c>Integrations:OutboundWebhooks</c> (INT-02).</summary>
public sealed class OutboundWebhookOptions
{
    public const string Section = "Integrations:OutboundWebhooks";

    /// <summary>Rede privada, <c>http</c> e outras portas: só vale em <c>Development</c> e nos testes (CA 16); em produção é ignorado.</summary>
    public bool AllowPrivateNetworks { get; set; }

    /// <summary>Envios simultâneos por inscrição (CA 13).</summary>
    public int MaxConcurrentPerSubscription { get; set; } = 4;

    /// <summary>Envios simultâneos por instância (CA 13).</summary>
    public int MaxConcurrentPerInstance { get; set; } = 32;
}

/// <summary>Seção <c>Integrations:Conecta</c>: o endereço que recebe os avisos do Conecta, os tipos padrão e os hosts aceitos (CA 1, 4 e 16).</summary>
public sealed class ConectaWebhookOptions
{
    public const string Section = "Integrations:Conecta";

    /// <summary>Vazio: a conexão nasce sem inscrição (o <c>webhook_secret</c> volta nulo).</summary>
    public string WebhookUrl { get; set; } = string.Empty;

    /// <summary>Vazio: todos os tipos do catálogo, limitados à concessão.</summary>
    public string[] DefaultEvents { get; set; } = [];

    /// <summary>Hosts aceitos para a URL da inscrição; vazio = só o host da <see cref="WebhookUrl"/>.</summary>
    public string[] WebhookAllowedHosts { get; set; } = [];

    public IReadOnlyList<string> AllowedHosts()
    {
        if (WebhookAllowedHosts.Length > 0)
        {
            return WebhookAllowedHosts;
        }

        return Uri.TryCreate(WebhookUrl, UriKind.Absolute, out var uri) ? [uri.Host] : [];
    }
}

/// <summary>Conexão recusada pelo guarda de rede (CA 16): mensagem segura, sem o corpo nem o segredo.</summary>
public sealed class OutboundAddressRefusedException(string message) : IOException(message);

/// <summary>
/// Proteção contra SSRF dos avisos de saída (CA 16): a URL é conferida ao gravar (só <c>https</c>, porta 443, sem usuário e senha, host na lista) e o
/// IP é conferido <b>a cada conexão</b>, no <c>ConnectCallback</c>, sobre o endereço de fato conectado.
/// </summary>
public static class OutboundUrlGuard
{
    public const string InvalidUrl = "Use um endereço https, na porta 443, sem usuário e senha.";
    public const string HostNotAllowed = "O endereço não está na lista de destinos permitidos.";
    public const string Refused = "Destino recusado: o endereço resolve para uma rede privada ou reservada.";

    /// <summary>Null quando a URL serve; senão o motivo.</summary>
    public static string? Validate(string? url, IReadOnlyCollection<string> allowedHosts, bool allowPrivate)
    {
        ArgumentNullException.ThrowIfNull(allowedHosts);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return InvalidUrl;
        }

        if (!allowPrivate && (uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443))
        {
            return InvalidUrl;
        }

        if (allowPrivate && uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            return InvalidUrl;
        }

        if (!allowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            return HostNotAllowed;
        }

        return !allowPrivate && IPAddress.TryParse(uri.Host.Trim('[', ']'), out var literal) && IsBlocked(literal) ? Refused : null;
    }

    /// <summary>Faixas recusadas (CA 16), com o IPv4 mapeado normalizado antes.</summary>
    public static bool IsBlocked(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            return true; // ::ffff:0:0/96 nunca é destino válido, seja qual for o IPv4 dentro
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] switch
            {
                0 or 10 or 127 => true,
                >= 224 => true, // multicast 224/4 e reservado 240/4 (inclui 255.255.255.255)
                100 => b[1] is >= 64 and <= 127, // CGNAT 100.64/10
                169 => b[1] == 254, // link-local
                172 => b[1] is >= 16 and <= 31,
                192 => (b[1] == 168) || (b[1] == 0 && b[2] == 0) || (b[1] == 0 && b[2] == 2), // 192.168/16, 192.0.0/24, TEST-NET-1
                198 => b[1] is 18 or 19 || (b[1] == 51 && b[2] == 100), // 198.18/15 e TEST-NET-2
                203 => b[1] == 0 && b[2] == 113, // TEST-NET-3
                _ => false,
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (IPAddress.IPv6Loopback.Equals(address) || IPAddress.IPv6Any.Equals(address) || address.IsIPv6Multicast || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal)
            {
                return true;
            }

            var b = address.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC)
            {
                return true; // fc00::/7
            }

            if (b[0] == 0x20 && b[1] == 0x02)
            {
                return true; // 6to4 2002::/16
            }

            if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && b.AsSpan(4, 8).IndexOfAnyExcept((byte)0) < 0)
            {
                return true; // NAT64 64:ff9b::/96
            }

            // IPv4 compatível (::a.b.c.d, obsoleto) também não sai.
            return b.AsSpan(0, 12).IndexOfAnyExcept((byte)0) < 0;
        }

        return true;
    }

    /// <summary>
    /// Handler do <c>HttpClient</c> <c>outbound-webhooks</c>: conexão 5 s, sem redirecionamento, sem cookies, sem proxy, TLS validado com SNI = host
    /// (padrão do <see cref="SocketsHttpHandler"/>), e o IP conferido no <c>ConnectCallback</c> antes de abrir o socket.
    /// </summary>
    public static SocketsHttpHandler CreateHandler(bool allowPrivate) => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectCallback = (context, cancellationToken) => ConnectAsync(context, allowPrivate, cancellationToken),
    };

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, bool allowPrivate, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host.Trim('[', ']'), out var literal) ? new[] { literal } : await Dns.GetHostAddressesAsync(host, cancellationToken);
        var allowed = addresses.Where(a => allowPrivate || !IsBlocked(a)).ToList();
        if (allowed.Count == 0)
        {
            throw new OutboundAddressRefusedException(Refused);
        }

        Exception? last = null;
        foreach (var address in allowed)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                // Confere o endereço de fato conectado (normalizado), não só o resolvido.
                if (!allowPrivate && socket.RemoteEndPoint is IPEndPoint remote && IsBlocked(remote.Address.IsIPv4MappedToIPv6 ? remote.Address.MapToIPv4() : remote.Address))
                {
                    throw new OutboundAddressRefusedException(Refused);
                }

                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is SocketException or OutboundAddressRefusedException)
            {
                socket.Dispose();
                last = ex;
            }
        }

        throw last is OutboundAddressRefusedException ? last : new IOException("Não foi possível conectar ao destino.", last);
    }
}
