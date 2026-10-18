using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BemAqui.Identity.Application;

/// <summary>TOTP da RFC 6238: HMAC-SHA1, intervalos de 30 s, 6 dígitos, tolerância de ±1 intervalo.</summary>
public static class Totp
{
    public const int Digits = 6;
    public const int StepSeconds = 30;

    /// <summary>160 bits, o tamanho recomendado para HMAC-SHA1.</summary>
    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(20);

    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / StepSeconds;

    public static string Code(byte[] secret, long step)
    {
        ArgumentNullException.ThrowIfNull(secret);
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[20];
        // A RFC 6238 usa HMAC-SHA1, que é o que os apps autenticadores aceitam. HMAC-SHA1 continua seguro; o fraco é o SHA-1 puro.
#pragma warning disable CA5350
        HMACSHA1.HashData(secret, counter, hash);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    /// <summary>Intervalo em que o código vale (agora, um antes ou um depois), ou null. Comparação em tempo fixo.</summary>
    public static long? Match(byte[] secret, string? code, DateTimeOffset now)
    {
        var digits = new string((code ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (digits.Length != Digits)
        {
            return null;
        }

        var current = StepAt(now);
        long? matched = null;
        for (var step = current - 1; step <= current + 1; step++)
        {
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(secret, step)), Encoding.ASCII.GetBytes(digits)))
            {
                matched ??= step;
            }
        }

        return matched;
    }

    /// <summary>URI <c>otpauth://</c> do QR code; o QR é desenhado no cliente, sem serviço externo.</summary>
    public static string OtpAuthUri(string issuer, string account, byte[] secret) =>
        "otpauth://totp/" + Uri.EscapeDataString(issuer) + ":" + Uri.EscapeDataString(account)
        + "?secret=" + Base32.Encode(secret) + "&issuer=" + Uri.EscapeDataString(issuer)
        + "&algorithm=SHA1&digits=" + Digits.ToString(CultureInfo.InvariantCulture) + "&period=" + StepSeconds.ToString(CultureInfo.InvariantCulture);

    /// <summary>Chave para digitar à mão, em grupos de 4.</summary>
    public static string ManualKey(byte[] secret) =>
        string.Join(' ', Base32.Encode(secret).Chunk(4).Select(c => new string(c)));
}

/// <summary>Base32 da RFC 4648, sem padding (formato dos apps autenticadores).</summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            output.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return output.ToString();
    }

    public static byte[] Decode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var clean = text.Replace(" ", string.Empty, StringComparison.Ordinal).TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            var value = Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (value < 0)
            {
                throw new FormatException("Caractere fora do Base32: " + c);
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return [.. output];
    }
}
