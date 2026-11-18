using Microsoft.AspNetCore.Builder;

namespace BemAqui.BuildingBlocks.Web;

/// <summary>
/// Marca o endpoint na lista permitida da sessão por PIN (ADR 0026 §4). Tudo o que não tem a marca responde 403
/// <c>pin_session_scope</c> para a sessão por PIN. Endpoint novo com a marca só entra com revisão: o teste de arquitetura
/// <c>PinSessionAllowListTests</c> compara cada uso com a lista revisada.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class PinSessionAllowedAttribute : Attribute;

/// <summary>
/// Lista permitida da sessão por PIN no aparelho de caixa do PDV (PDV-01 CA 11, ADR 0048): separada da lista da fábrica. A sessão por PIN do caixa só
/// alcança os endpoints com esta marca, todos em <c>/api/v1/pos/*</c> e no módulo <c>pos</c>; a sessão por PIN da fábrica nunca os alcança, e a do caixa
/// nunca alcança os do <c>/fab</c>. Endpoint novo com a marca só entra com revisão (<c>PinSessionAllowListTests</c> e <c>EndpointAuthorizationTests</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class PosPinSessionAllowedAttribute : Attribute;

public static class PinSessionEndpoints
{
    public const string ScopeCode = "pin_session_scope";
    public const string ScopeMessage = "Esta função não está disponível na entrada por PIN. Entre com e-mail e senha.";
    public const string PosScopeMessage = "Esta função não está disponível no caixa. Quem precisa da retaguarda entra com e-mail e senha.";

    /// <summary>Limite da entrada por PIN no caixa (política nomeada, por tenant, IP e aparelho): o bloqueio por PIN e por aparelho vem antes.</summary>
    public const string PosPinLoginRateLimitPolicy = "pos-pin-login";

    /// <summary>Prefixo das rotas que a sessão por PIN do caixa pode alcançar.</summary>
    public const string PosPathPrefix = "/api/v1/pos/";

    /// <summary>Põe o endpoint (ou o hub) na lista permitida da sessão por PIN: <c>[PinSessionAllowed]</c>.</summary>
    public static TBuilder AllowPinSession<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(new PinSessionAllowedAttribute());
    }

    /// <summary>Põe o endpoint na lista permitida da sessão por PIN do caixa do PDV: <c>[PosPinSessionAllowed]</c>.</summary>
    public static TBuilder AllowPosPinSession<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(new PosPinSessionAllowedAttribute());
    }
}
