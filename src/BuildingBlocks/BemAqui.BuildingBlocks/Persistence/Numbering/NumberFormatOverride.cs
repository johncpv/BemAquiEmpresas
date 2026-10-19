using BemAqui.SharedKernel;

namespace BemAqui.BuildingBlocks.Persistence.Numbering;

/// <summary>
/// Formato de numeração que o tenant mudou (<c>shared.number_formats</c>, CAD-06 CA 10): só o que difere do padrão de
/// <see cref="NumberFormats.Defaults"/>. Auditado: o histórico mostra quem trocou o prefixo e quando.
/// </summary>
public sealed class NumberFormatOverride : Entity, IAuditable
{
    public required string DocType { get; init; }

    public string Prefix { get; set; } = string.Empty;

    public bool Yearly { get; set; }

    public int Digits { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public NumberFormat ToFormat() => new(Prefix, Yearly, Digits);
}

/// <summary>Linha da aba Numeração (CA 9): formato efetivo, último emitido no período atual e a prévia do próximo.</summary>
public sealed record NumberFormatRow(string DocType, string Label, string Prefix, bool Yearly, int Digits, long LastIssued, bool HasIssued, string Next, bool Customized);

/// <summary>O que a tela muda (CA 10 e 11). <c>NextNumber</c> só aumenta, para continuar a numeração do sistema antigo.</summary>
public sealed record NumberFormatInput(string Prefix, bool Yearly, int Digits, long? NextNumber = null);

/// <summary>Administração da numeração por tenant (ADR 0016), usada por Configurações → Numeração.</summary>
public interface INumberFormatAdmin
{
    Task<IReadOnlyList<NumberFormatRow>> ListAsync(CancellationToken cancellationToken = default);

    Task<Result<NumberFormatRow>> SaveAsync(string docType, NumberFormatInput input, CancellationToken cancellationToken = default);
}
