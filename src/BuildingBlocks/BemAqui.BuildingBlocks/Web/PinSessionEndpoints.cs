using Microsoft.AspNetCore.Builder;

namespace BemAqui.BuildingBlocks.Web;

/// <summary>
/// Marca o endpoint na lista permitida da sessão por PIN (ADR 0026 §4). Tudo o que não tem a marca responde 403
/// <c>pin_session_scope</c> para a sessão por PIN. Endpoint novo com a marca só entra com revisão: o teste de arquitetura
/// <c>PinSessionAllowListTests</c> compara cada uso com a lista revisada.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class PinSessionAllowedAttribute : Attribute;

public static class PinSessionEndpoints
{
    public const string ScopeCode = "pin_session_scope";
    public const string ScopeMessage = "Esta função não está disponível na entrada por PIN. Entre com e-mail e senha.";

    /// <summary>Põe o endpoint (ou o hub) na lista permitida da sessão por PIN: <c>[PinSessionAllowed]</c>.</summary>
    public static TBuilder AllowPinSession<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.WithMetadata(new PinSessionAllowedAttribute());
    }
}