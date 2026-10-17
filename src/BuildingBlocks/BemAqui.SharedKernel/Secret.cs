namespace BemAqui.SharedKernel;

/// <summary>
/// Valor sensível (senha, chave de API, segredo TOTP, connection string). Nunca aparece em
/// <see cref="ToString"/>, log ou auditoria: o texto só sai por <see cref="Reveal"/>, no ponto de uso.
/// DTO de resposta nunca tem propriedade deste tipo (write-only, ADR 0007).
/// </summary>
public sealed class Secret : IEquatable<Secret>
{
    public const string Mask = "***";

    private readonly string _value;

    public Secret(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        _value = value;
    }

    /// <summary>Texto do segredo. Use só onde ele é efetivamente consumido (conexão, envio, verificação).</summary>
    public string Reveal() => _value;

    public override string ToString() => Mask;

    public bool Equals(Secret? other) => other is not null && string.Equals(_value, other._value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as Secret);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_value);
}
