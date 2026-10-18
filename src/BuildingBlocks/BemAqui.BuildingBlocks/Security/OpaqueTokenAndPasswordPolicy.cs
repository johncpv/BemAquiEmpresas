using System.Security.Cryptography;
using System.Text;

namespace BemAqui.BuildingBlocks.Security;

/// <summary>Tokens opacos (refresh, convite, redefinição): 32 bytes aleatórios; no banco fica só o SHA-256.</summary>
public static class OpaqueToken
{
    public static string New() => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}

/// <summary>
/// Política de senha (ADR 0010): no mínimo 12 caracteres, sem regras de composição, e fora da lista local de
/// senhas vazadas ou óbvias.
/// </summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 12;

    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "123456789012", "1234567890123", "123123123123", "qwertyuiop12", "qwertyuiopas", "password1234", "senha1234567",
        "senhasenha12", "admin1234567", "administrator", "iloveyou1234", "abcdefghijkl", "aaaaaaaaaaaa", "000000000000",
        "111111111111", "minhasenha12", "trocarsenha1", "bemaqui12345", "konebras1234", "brasil123456", "mudar1234567",
    };

    /// <summary>Mensagem do motivo em PT-BR, ou null se a senha é aceita.</summary>
    public static string? Validate(string? password, string? email = null)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinimumLength)
        {
            return "A senha precisa ter pelo menos 12 caracteres. Uma frase fácil de lembrar funciona bem.";
        }

        if (password.Length > 128)
        {
            return "A senha pode ter no máximo 128 caracteres.";
        }

        if (Common.Contains(password) || password.Distinct().Count() <= 2)
        {
            return "Essa senha é muito comum. Escolha outra.";
        }

        if (email is not null && password.Contains(email.Split('@')[0], StringComparison.OrdinalIgnoreCase) && email.Split('@')[0].Length >= 4)
        {
            return "A senha não pode conter o seu e-mail.";
        }

        return null;
    }
}
