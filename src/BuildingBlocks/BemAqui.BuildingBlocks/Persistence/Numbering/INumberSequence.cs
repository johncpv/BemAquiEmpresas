namespace BemAqui.BuildingBlocks.Persistence.Numbering;

/// <summary>
/// Próximo número legível de um tipo de documento (ex.: <c>ORC-2026-0001</c>). Roda na conexão e transação
/// do DbContext do tenant: se o documento não for salvo (rollback), o número volta e não fica lacuna.
/// </summary>
public interface INumberSequence
{
    Task<string> NextAsync(string docType, CancellationToken cancellationToken = default);
}
