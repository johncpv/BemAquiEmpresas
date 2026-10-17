namespace BemAqui.BuildingBlocks.Persistence.Numbering;

/// <summary>Último número emitido por tipo de documento e ano (ano 0 = sequência sem ano, ex.: PAT).</summary>
public sealed class NumberSequence
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string DocType { get; init; }

    public int Year { get; init; }

    public long LastValue { get; set; }
}
