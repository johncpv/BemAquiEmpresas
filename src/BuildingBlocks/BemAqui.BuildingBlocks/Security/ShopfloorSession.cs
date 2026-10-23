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
}

/// <summary>Cookie do aparelho registrado (ADR 0026 §1, ADR 0009 adendo): <c>__Host-</c>, HttpOnly, Secure, SameSite=Strict, Path=/.</summary>
public static class ShopfloorDeviceCookie
{
    public const string Name = "__Host-bq_dev";
}