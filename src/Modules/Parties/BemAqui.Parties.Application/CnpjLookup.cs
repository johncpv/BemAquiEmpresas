using BemAqui.SharedKernel;

namespace BemAqui.Parties.Application;

/// <summary>Endereço que a Receita devolve para o CNPJ (os campos que faltam ficam vazios para o usuário completar).</summary>
public sealed record CnpjAddress(string? ZipCode, string? Street, string? Number, string? Complement, string? District, string? City, string? State, string? IbgeCode);

/// <summary>
/// Dados públicos do CNPJ (CAD-01b CA 1 e 3), sem o quadro societário. <c>Situation</c> é a situação cadastral como a
/// Receita escreve ("ATIVA", "BAIXADA"...); fora de "ATIVA", a tela só avisa.
/// </summary>
public sealed record CnpjData(string Document, string LegalName, string? TradeName, string? Situation, CnpjAddress? Address, string? Phone, string? Email)
{
    public bool ActiveAtReceita => string.Equals(Situation, "ATIVA", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Resposta da consulta: os dados, quando foram consultados e se vieram do cache (CA 4).</summary>
public sealed record CnpjLookupResult(CnpjData Data, DateTimeOffset LookedUpAt, bool FromCache);

/// <summary>Provedor de CNPJ (CA 15): trocar por configuração não muda a tela nem o cadastro.</summary>
public interface ICnpjLookup
{
    /// <summary>NotFound "cnpj_not_found" ou Unavailable "lookup_unavailable" quando não há dados.</summary>
    Task<Result<CnpjData>> LookupAsync(string cnpj, CancellationToken cancellationToken = default);
}
