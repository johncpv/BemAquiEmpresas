using System.Net;
using System.Net.Sockets;
using BemAqui.BuildingBlocks.Web;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.HttpOverrides;

namespace BemAqui.Host.Security;

/// <summary>
/// Seção <c>ReverseProxy</c>: só o stack-nginx é proxy confiável (FND-02 CA 9, D19).
/// Em homologação e produção vale o nome do container (<see cref="KnownProxyHosts"/>), não a sub-rede inteira da
/// stack_stack, porque outros projetos na mesma rede poderiam forjar X-Forwarded-For.
/// </summary>
public sealed class ReverseProxyOptions
{
    public const string Section = "ReverseProxy";

    /// <summary>Redes do proxy (CIDR). Para dev e testes; vazio: nenhuma rede é confiável.</summary>
    public string[] KnownNetworks { get; set; } = [];

    /// <summary>Endereços IP do proxy.</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>Nomes do proxy (ex.: <c>stack-nginx</c>), resolvidos por DNS na subida e de novo quando o IP do container muda.</summary>
    public string[] KnownProxyHosts { get; set; } = [];

    /// <summary>Recusa (404) quem não vem do proxy: acesso direto ao container não resolve tenant.</summary>
    public bool RequireTrustedProxy { get; set; }

    public IReadOnlyList<System.Net.IPNetwork> Networks() =>
        KnownNetworks.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => System.Net.IPNetwork.Parse(n.Trim())).ToList();

    public IReadOnlyList<IPAddress> Addresses() =>
        KnownProxies.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => Normalize(IPAddress.Parse(a.Trim()))).ToList();

    public IReadOnlyList<string> Hosts() =>
        KnownProxyHosts.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim()).ToList();

    internal static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}

/// <summary>
/// Conjunto de proxies confiáveis: redes, IPs fixos e os IPs dos nomes em <c>KnownProxyHosts</c>.
/// Os nomes são resolvidos na subida (com log do IP). Se chegar requisição de IP desconhecido, resolve de novo no
/// máximo a cada <see cref="RefreshInterval"/>: o stack-nginx recriado ganha outro IP e o site não pode cair por isso.
/// </summary>
public sealed partial class TrustedProxies
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    private readonly IReadOnlyList<System.Net.IPNetwork> networks;
    private readonly IReadOnlyList<IPAddress> addresses;
    private readonly IReadOnlyList<string> hosts;
    private readonly Func<string, IPAddress[]> resolve;
    private readonly TimeProvider clock;
    private readonly ILogger logger;
    private readonly Lock gate = new();
    private HashSet<IPAddress> resolved = [];
    private DateTimeOffset resolvedAt;

    public TrustedProxies(ReverseProxyOptions options, ILogger<TrustedProxies> logger, TimeProvider? clock = null, Func<string, IPAddress[]>? resolve = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        networks = options.Networks();
        addresses = options.Addresses();
        hosts = options.Hosts();
        this.logger = logger;
        this.clock = clock ?? TimeProvider.System;
        this.resolve = resolve ?? Dns.GetHostAddresses;
        Refresh();
    }

    /// <summary>IPs atuais dos nomes configurados.</summary>
    public IReadOnlySet<IPAddress> ResolvedAddresses => resolved;

    public bool IsTrusted(IPAddress? remote)
    {
        if (remote is null)
        {
            return false;
        }

        var address = ReverseProxyOptions.Normalize(remote);
        if (Matches(address))
        {
            return true;
        }

        if (hosts.Count == 0 || clock.GetUtcNow() - resolvedAt < RefreshInterval)
        {
            return false;
        }

        Refresh();
        return resolved.Contains(address);
    }

    private bool Matches(IPAddress address) =>
        IPAddress.IsLoopback(address)
        || addresses.Contains(address)
        || networks.Any(n => n.Contains(address))
        || resolved.Contains(address);

    private void Refresh()
    {
        lock (gate)
        {
            var next = new HashSet<IPAddress>();
            foreach (var host in hosts)
            {
                try
                {
                    var ips = resolve(host).Select(ReverseProxyOptions.Normalize).ToList();
                    next.UnionWith(ips);
                    var list = string.Join(", ", ips);
                    LogResolved(logger, host, list);
                }
                catch (SocketException ex)
                {
                    // Migrator e importer não estão na stack_stack: o nome não resolve e nenhum IP é confiável.
                    LogNotResolved(logger, ex, host);
                }
            }

            resolved = next;
            resolvedAt = clock.GetUtcNow();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Proxy confiável {Host} resolvido para {Enderecos}")]
    private static partial void LogResolved(ILogger logger, string host, string enderecos);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Proxy confiável {Host} não resolveu. Cabeçalhos encaminhados dele serão ignorados")]
    private static partial void LogNotResolved(ILogger logger, Exception exception, string host);
}

public static class ReverseProxySetup
{
    /// <summary>
    /// X-Forwarded-For/Proto só de proxy confiável; Host nunca vem de cabeçalho (o nginx envia o Host real).
    /// A decisão de confiança é do <see cref="TrustedProxies"/> (ver <see cref="UseTrustedForwardedHeaders"/>), por isso
    /// aqui a lista do próprio middleware aceita qualquer origem e o ramo só roda para o proxy.
    /// </summary>
    public static void Configure(ForwardedHeadersOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("0.0.0.0/0"));
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("::/0"));
        options.ForwardLimit = 1;
    }

    /// <summary>Aplica os cabeçalhos encaminhados só quando o socket é de um proxy confiável.</summary>
    public static IApplicationBuilder UseTrustedForwardedHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var trusted = app.ApplicationServices.GetRequiredService<TrustedProxies>(); // resolve os nomes já na subida
        var options = new ForwardedHeadersOptions();
        Configure(options);
        return app.UseWhen(context => trusted.IsTrusted(context.Connection.RemoteIpAddress), branch => branch.UseForwardedHeaders(options));
    }
}

/// <summary>
/// Com <c>RequireTrustedProxy</c>, requisição que não veio do proxy responde 404 (exceto /health para o healthcheck do Docker e o webhook no
/// host interno da API: o EmissorFiscal avisa direto pela rede dedicada <c>bqe-fiscal</c>, sem o nginx, FIS-01 CA 11 e 17; a origem continua
/// provada pela assinatura, e só o provedor que declara <c>AllowInternalHost</c> é aceito ali). Registrar antes do UseForwardedHeaders.
/// </summary>
public sealed class TrustedProxyMiddleware(
    RequestDelegate next,
    Microsoft.Extensions.Options.IOptions<ReverseProxyOptions> options,
    TrustedProxies trusted,
    Microsoft.Extensions.Options.IOptions<BemAqui.BuildingBlocks.Webhooks.WebhookOptions> webhooks)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        // Roda antes do UseForwardedHeaders: aqui o RemoteIpAddress ainda é o do socket.
        if (options.Value.RequireTrustedProxy
            && !context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
            && !trusted.IsTrusted(context.Connection.RemoteIpAddress)
            && !IsInternalWebhook(context))
        {
            await Error.NotFound(Error.Codes.NotFound, "Endereço não encontrado.").ToProblem().ExecuteAsync(context);
            return;
        }

        await next(context);
    }

    private bool IsInternalWebhook(HttpContext context) =>
        context.Request.Path.StartsWithSegments(BemAqui.BuildingBlocks.Webhooks.WebhookEndpoints.PathPrefix, StringComparison.OrdinalIgnoreCase)
        && BemAqui.BuildingBlocks.Webhooks.WebhookEndpoints.IsInternalHost(context, webhooks.Value);
}
