using BemAqui.Parties.Contracts;

namespace BemAqui.Pos.Application;

/// <summary>
/// Cliente cadastrado achado pelo caixa (PDV-02 CA 15): id, nome e o documento já mascarado (o CPF mostra só os 3 últimos dígitos antes do verificador; o CNPJ, que é
/// da empresa, sai formatado). Nada além disso desce para o aparelho.
/// </summary>
public sealed record PosCustomer(Guid Id, string Name, string? Document);

/// <summary>
/// Busca do cliente cadastrado na tela de venda (PDV-02 CA 15), pelo contrato de Terceiros (<see cref="IParties"/>): só clientes ativos, no máximo
/// 10, com 3 letras ou mais. Só com rede: sem ela o caixa desabilita a busca e o CPF avulso continua valendo.
/// </summary>
public sealed class PosCustomerSearchService(IParties parties)
{
    public const int MinTermLength = 3;
    public const int Limit = 10;

    /// <summary>
    /// Política nomeada de rate limit da busca (revisão de segurança da PDV-02, achado 1): por tenant, usuário e aparelho, 20 por minuto,
    /// configurável em <c>RateLimit:Auth:PosCustomerSearchPerMinute</c>. Corta a mineração da base de clientes pelo caixa.
    /// </summary>
    public const string RateLimitPolicy = "pos-customer-search";

    public async Task<IReadOnlyList<PosCustomer>> SearchAsync(string? term, CancellationToken cancellationToken)
    {
        var text = term?.Trim() ?? string.Empty;
        if (text.Length < MinTermLength)
        {
            return [];
        }

        var found = await parties.SearchAsync(text, PartyRoleKind.Customer, Limit, cancellationToken);
        return found.Where(p => p.IsActive && !p.IsDeleted)
            .Select(p => new PosCustomer(p.Id, string.IsNullOrWhiteSpace(p.TradeName) ? p.LegalName : p.TradeName!, Mask(p.Kind, p.Document)))
            .ToList();
    }

    /// <summary>CPF "529.982.247-25" sai "***.***.247-**" (só os 3 últimos antes do verificador); CNPJ sai formatado e inteiro; outro formato, nada.</summary>
    public static string? Mask(PartyKind kind, string? document)
    {
        if (string.IsNullOrEmpty(document))
        {
            return null;
        }

        if (kind == PartyKind.PF && document.Length == 11)
        {
            return $"***.***.{document[6..9]}-**";
        }

        return kind == PartyKind.PJ && document.Length == 14
            ? $"{document[..2]}.{document[2..5]}.{document[5..8]}/{document[8..12]}-{document[12..]}"
            : null;
    }
}
