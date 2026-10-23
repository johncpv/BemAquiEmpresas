using System.Globalization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.Identity.Domain;

namespace BemAqui.Identity.Application.Shopfloor;

/// <summary>
/// Regras do PIN (ADR 0026 §2, PRD-03b CA 12): 6 dígitos e nada óbvio (todos iguais, sequência crescente ou decrescente,
/// padrão repetido como 121212 ou 123123, e a data de nascimento do funcionário em qualquer formato comum de 6 dígitos).
/// </summary>
public static class PinRules
{
    public const string FormatMessage = "O PIN tem 6 números.";
    public const string MismatchMessage = "Os dois PINs não são iguais. Digite de novo.";
    public const string ObviousMessage = "Esse PIN é fácil de adivinhar. Evite números repetidos, sequências e a data de nascimento.";

    /// <summary>Mensagem do problema, ou null quando o PIN serve.</summary>
    public static string? Problem(string? pin, string? confirmation, DateOnly? birthDate)
    {
        if (!IsWellFormed(pin))
        {
            return FormatMessage;
        }

        if (!string.Equals(pin, confirmation, StringComparison.Ordinal))
        {
            return MismatchMessage;
        }

        return IsObvious(pin!, birthDate) ? ObviousMessage : null;
    }

    public static bool IsWellFormed(string? pin) => pin is { Length: UserPin.Length } && pin.All(char.IsAsciiDigit);

    public static bool IsObvious(string pin, DateOnly? birthDate)
    {
        ArgumentNullException.ThrowIfNull(pin);
        var digits = pin.Select(c => c - '0').ToArray();
        var steps = digits.Zip(digits.Skip(1), (a, b) => (b - a + 10) % 10).Distinct().ToList();
        if (steps.Count == 1 && steps[0] is 0 or 1 or 9)
        {
            return true; // 000000, 123456, 890123 (dá a volta), 987654
        }

        if (pin[..2] == pin[2..4] && pin[2..4] == pin[4..] || pin[..3] == pin[3..])
        {
            return true; // 121212, 123123
        }

        return birthDate is { } d && BirthDateForms(d).Contains(pin, StringComparer.Ordinal);
    }

    /// <summary>A data de nascimento nos formatos de 6 dígitos que as pessoas usam (ddMMyy, MMddyy, yyMMdd, MMyyyy, yyyyMM).</summary>
    public static IEnumerable<string> BirthDateForms(DateOnly date) =>
        BirthFormats.Select(f => date.ToString(f, CultureInfo.InvariantCulture));

    private static readonly string[] BirthFormats = ["ddMMyy", "MMddyy", "yyMMdd", "MMyyyy", "yyyyMM"];
}

/// <summary>
/// Perfil sensível (ADR 0008 adendo, ADR 0026 §2, PRD-03b CA 17): quem tem <c>*.approve</c>, <c>finance.pay</c>,
/// <c>hr.view_sensitive</c>, <c>hr.view_health</c>, <c>identity.edit</c> ou <c>settings.edit</c> (ou é do Proprietário, que tem tudo)
/// não usa PIN. A exceção é a <c>production.approve_quality</c> (D6), que não é <c>.approve</c>.
/// </summary>
public static class PinProfile
{
    public const string NotAllowedMessage = "Seu perfil não pode usar PIN.";
    public const string ChangedMessage = "Seu perfil mudou e não pode usar PIN. Entre com e-mail e senha.";

    private static readonly string[] Reserved = ["finance.pay", "hr.view_sensitive", "hr.view_health", "identity.edit", "settings.edit"];

    public static bool IsSensitive(string permission)
    {
        ArgumentNullException.ThrowIfNull(permission);
        return permission.EndsWith(".approve", StringComparison.Ordinal) || Reserved.Contains(permission, StringComparer.Ordinal);
    }

    /// <summary>As ações que impedem o PIN (para a auditoria); vazio quando o perfil pode usar PIN.</summary>
    public static IReadOnlyList<string> SensitiveActions(EffectivePermissions permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        return permissions.IsOwner ? ["owner"] : permissions.Permissions.Where(IsSensitive).Order(StringComparer.Ordinal).ToList();
    }

    public static bool CanUsePin(EffectivePermissions permissions) =>
        permissions.Has("shopfloor.create") && SensitiveActions(permissions).Count == 0;
}

/// <summary>Prazos da sessão (ADR 0026 §3): completa com renovação de 7 dias até o prazo absoluto; por PIN, 12 h sem renovação além.</summary>
public static class SessionLifetimes
{
    public static DateTimeOffset FullAbsolute(DateTimeOffset familyStart, int days) =>
        familyStart + TimeSpan.FromDays(days is > 0 and <= 365 ? days : RefreshToken.DefaultAbsoluteDays);

    public static DateTimeOffset PinAbsolute(DateTimeOffset start) => start + RefreshToken.PinSessionLifetime;

    /// <summary>Validade do refresh novo: a renovação deslizante, sem passar do prazo absoluto.</summary>
    public static DateTimeOffset RefreshExpiry(DateTimeOffset now, TimeSpan sliding, DateTimeOffset absolute) =>
        now + sliding < absolute ? now + sliding : absolute;
}

/// <summary>Nome na lista do <c>/fab/entrar</c> (ponto 69): só primeiro nome e inicial.</summary>
public static class PinNames
{
    /// <summary>"Diego Santos" vira "Diego S.".</summary>
    public static string ShortName(string name)
    {
        var parts = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => "Operador",
            1 => parts[0],
            _ => parts[0] + " " + char.ToUpperInvariant(parts[^1][0]) + ".",
        };
    }
}