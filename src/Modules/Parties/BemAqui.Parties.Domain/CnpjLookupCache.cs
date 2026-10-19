namespace BemAqui.Parties.Domain;

/// <summary>
/// Cache da consulta de CNPJ (<c>parties.cnpj_lookups</c>, CAD-01b CA 4 e 14): por tenant, sem o quadro societário. A razão
/// social de MEI contém nome e CPF, então o conteúdo é tratado como dado pessoal possível e apagado aos 30 dias.
/// </summary>
public sealed class CnpjLookupCache
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public string Document { get; init; } = string.Empty;

    /// <summary>JSON dos dados (<c>jsonb</c>).</summary>
    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset LookedUpAt { get; set; }
}
