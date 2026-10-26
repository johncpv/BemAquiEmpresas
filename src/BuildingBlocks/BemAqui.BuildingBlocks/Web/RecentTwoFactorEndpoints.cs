using System.Globalization;
using System.Security.Claims;
using BemAqui.BuildingBlocks.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.BuildingBlocks.Web;

/// <summary>Metadado do endpoint: exige a prova recente do 2FA (BNK-01a CA 1). O teste da lista da D6 confere a marca.</summary>
public sealed record RequiresRecentTwoFactorMetadata;

/// <summary>
/// "2FA de novo" (D6, ADR 0008 adendo de 21/10/2026; BNK-01a CA 1): a ação só passa com o access token que traz a prova recente do
/// código TOTP, a claim <c>otp_at</c> (instante da última confirmação, em segundos Unix), há até <see cref="Window"/>. A prova sai no
/// login com o código e no <c>POST /api/v1/me/2fa/step-up</c>, que troca só o access token (a sessão e o refresh continuam os mesmos);
/// a renovação silenciosa do token não carrega a claim, então a prova vence sozinha. Sem prova recente, 403 <c>two_factor_required</c>
/// com <c>enrolled</c> (o usuário tem o 2FA ativo?), e o front abre o passo "Confirme o código" e repete a ação.
/// </summary>
public static class RecentTwoFactorEndpoints
{
    public const string Code = "two_factor_required";
    public const string ConfirmMessage = "Para continuar, confirme o código da verificação em duas etapas.";
    public const string EnrollMessage = "Para continuar, ative a verificação em duas etapas.";

    /// <summary>Política de rate limit do <c>POST /api/v1/me/2fa/step-up</c> (por usuário e tenant).</summary>
    public const string StepUpRateLimitPolicy = "two-factor-step-up";

    /// <summary>Janela da prova (CA 1: até 15 minutos).</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    /// <summary>Tolerância para relógio adiantado no emissor (o token é do próprio servidor; só corta valor absurdo).</summary>
    private static readonly TimeSpan FutureSkew = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Marca o endpoint e põe o filtro. Use antes do <c>RequireIdempotencyKey</c>, para a recusa não gastar a chave e a mesma
    /// requisição poder ser repetida depois da confirmação.
    /// </summary>
    public static TBuilder RequireRecentTwoFactor<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.WithMetadata(new RequiresRecentTwoFactorMetadata());
        builder.AddEndpointFilter(async (context, next) =>
        {
            var clock = context.HttpContext.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;
            return HasRecentProof(context.HttpContext.User, clock.GetUtcNow()) ? await next(context) : Refused(context.HttpContext.User);
        });
        return builder;
    }

    /// <summary>A prova do token vale agora? (instante da claim <c>otp_at</c> dentro da janela).</summary>
    public static bool HasRecentProof(ClaimsPrincipal user, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.Identity?.IsAuthenticated != true || user.FindAll(AuthMethods.Claim).Any(c => c.Value == AuthMethods.Pin))
        {
            return false;
        }

        var value = user.FindFirst(AuthMethods.OtpAtClaim)?.Value;
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        var at = DateTimeOffset.FromUnixTimeSeconds(seconds);
        return at <= now + FutureSkew && now - at <= Window;
    }

    /// <summary>Usuário com o 2FA ativo (o token traz <c>otp</c> no <c>amr</c> só quando o 2FA está ligado).</summary>
    public static bool IsEnrolled(ClaimsPrincipal user) =>
        user?.FindAll(AuthMethods.Claim).Any(c => c.Value == AuthMethods.Otp) == true;

    public static IResult Refused(ClaimsPrincipal user)
    {
        var enrolled = IsEnrolled(user);
        return Results.Problem(
            detail: enrolled ? ConfirmMessage : EnrollMessage,
            statusCode: StatusCodes.Status403Forbidden,
            title: "Acesso negado",
            extensions: new Dictionary<string, object?> { ["code"] = Code, ["enrolled"] = enrolled });
    }
}
