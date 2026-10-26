namespace BemAqui.BuildingBlocks.Security;

/// <summary>
/// Como a sessão foi aberta (claim <c>amr</c>, ADR 0008, adendo de 21/10/2026): <c>pwd</c> (senha), <c>otp</c> (2FA) e <c>pin</c>
/// (aparelho da fábrica, ADR 0026). O token continua só com identidade; a sessão por PIN só alcança a lista permitida.
/// </summary>
public static class AuthMethods
{
    public const string Claim = "amr";
    public const string Password = "pwd";
    public const string Otp = "otp";
    public const string Pin = "pin";

    /// <summary>
    /// Instante (segundos Unix) da última confirmação do código TOTP nesta sessão: no login com o código e no "2FA de novo"
    /// (BNK-01a CA 1). Não é dado de identidade nem de permissão: só a prova que o <c>RequireRecentTwoFactor</c> confere.
    /// </summary>
    public const string OtpAtClaim = "otp_at";
}

/// <summary>Cookie do aparelho registrado (ADR 0026 §1, ADR 0009 adendo): <c>__Host-</c>, HttpOnly, Secure, SameSite=Strict, Path=/.</summary>
public static class ShopfloorDeviceCookie
{
    public const string Name = "__Host-bq_dev";
}