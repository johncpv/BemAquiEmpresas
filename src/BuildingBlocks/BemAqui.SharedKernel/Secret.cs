using System.Security.Cryptography;
using System.Text;

namespace BemAqui.SharedKernel;

/// <summary>
/// Valor sensível (senha, chave de API, segredo TOTP, connection string). Nunca aparece em
/// <see cref="ToString"/>, log ou auditoria: o texto só sai por <see cref="Reveal"/>, no ponto de uso.
/// DTO de resposta nunca tem propriedade deste tipo (write-only, ADR 0007).
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(SecretJsonConverter))]
public sealed class Secret : IEquatable<Secret>
{
    public const string Mask = "***";

    private readonly string _value;

    public Secret(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        if (value == Mask)
        {
            throw new ArgumentException("A máscara '***' não é um segredo.", nameof(value));
        }

        _value = value;
    }

    /// <summary>Texto do segredo. Use só onde ele é efetivamente consumido (conexão, envio, verificação).</summary>
    public string Reveal() => _value;

    public override string ToString() => Mask;

    /// <summary>Comparação em tempo constante (não vaza o conteúdo por tempo de resposta).</summary>
    public bool Equals(Secret? other) =>
        other is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(_value), Encoding.UTF8.GetBytes(other._value));

    public override bool Equals(object? obj) => Equals(obj as Secret);

    // Constante de propósito: o hash não pode carregar informação do valor.
    public override int GetHashCode() => 0;
}
