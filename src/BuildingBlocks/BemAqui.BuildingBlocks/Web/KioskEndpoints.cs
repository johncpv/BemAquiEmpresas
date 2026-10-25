using BemAqui.BuildingBlocks.Security;
using Microsoft.AspNetCore.Builder;

namespace BemAqui.BuildingBlocks.Web;

/// <summary>
/// Marca o endpoint (ou o hub) do Painel da TV: aceita só o principal de quiosque (esquema <c>Kiosk</c>, policy <c>kiosk</c>), nunca um
/// usuário. Fora desses endpoints o principal de quiosque recebe 403 <c>kiosk_scope</c> (ADR 0030 §2). Só em <c>GET /api/v1/kiosk/*</c>
/// e no <c>KioskHub</c>: o teste de arquitetura <c>KioskRulesTests</c> confere cada uso.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class KioskEndpointAttribute : Attribute;

public static class KioskEndpoints
{
    public const string PathPrefix = "/api/v1/kiosk";
    public const string HubPath = "/hubs/kiosk";
    public const string SessionPath = "/api/v1/kiosk/session";
    public const string SessionRateLimitPolicy = "kiosk-session";
    public const string ScopeCode = "kiosk_scope";
    public const string ScopeMessage = "A tela de TV só mostra o painel.";

    /// <summary>Exige a sessão de quiosque (esquema <c>Kiosk</c>) e marca o endpoint como do painel.</summary>
    public static TBuilder RequireKiosk<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WithMetadata(new KioskEndpointAttribute());
        return builder.RequireAuthorization(KioskClaims.Policy);
    }
}
