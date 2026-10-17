using System.Net;
using BemAqui.BuildingBlocks.Web;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.HttpOverrides;

namespace BemAqui.Host.Security;

/// <summary>Seção <c>ReverseProxy</c>: só o stack-nginx (rede stack_stack) é proxy confiável (FND-02 CA 9).</summary>
public sealed class ReverseProxyOptions
{
    public const string Section = "ReverseProxy";

    /// <summary>Redes do proxy (CIDR), ex.: a sub-rede da stack_stack. Vazio: nenhum cabeçalho encaminhado é aceito.</summary>
    public string[] KnownNetworks { get; set; } = [];

    /// <summary>Recusa (404) quem não vem do proxy: acesso direto ao container não resolve tenant.</summary>
    public bool RequireTrustedProxy { get; set; }

    public IReadOnlyList<System.Net.IPNetwork> Networks() =>
        KnownNetworks.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => System.Net.IPNetwork.Parse(n.Trim())).ToList();
}

public static class ReverseProxySetup
{
    /// <summary>X-Forwarded-For/Proto só das redes configuradas; Host nunca vem de cabeçalho (o nginx envia o Host real).</summary>
    public static void Configure(ForwardedHeadersOptions options, ReverseProxyOptions proxy)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(proxy);
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        options.ForwardLimit = 1;
        foreach (var network in proxy.Networks())
        {
            options.KnownIPNetworks.Add(network);
        }
    }

    public static bool IsTrusted(IPAddress? remote, ReverseProxyOptions proxy)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        if (remote is null)
        {
            return false;
        }

        var address = remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote;
        return IPAddress.IsLoopback(address) || proxy.Networks().Any(n => n.Contains(address));
    }
}

/// <summary>Com <c>RequireTrustedProxy</c>, requisição que não veio do proxy responde 404 (exceto /health para o healthcheck do Docker). Registrar antes do UseForwardedHeaders.</summary>
public sealed class TrustedProxyMiddleware(RequestDelegate next, Microsoft.Extensions.Options.IOptions<ReverseProxyOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var proxy = options.Value;
        // Roda antes do UseForwardedHeaders: aqui o RemoteIpAddress ainda é o do socket.
        var socketIp = context.Connection.RemoteIpAddress;
        if (proxy.RequireTrustedProxy
            && !context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
            && !ReverseProxySetup.IsTrusted(socketIp, proxy))
        {
            await Error.NotFound(Error.Codes.NotFound, "Endereço não encontrado.").ToProblem().ExecuteAsync(context);
            return;
        }

        await next(context);
    }
}
