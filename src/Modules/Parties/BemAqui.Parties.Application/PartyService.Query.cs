using BemAqui.BuildingBlocks.Paging;
using BemAqui.Parties.Contracts;
using BemAqui.Parties.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Parties.Application;

public sealed partial class PartyService
{
    /// <summary>
    /// Lista (CA 16 a 18): busca por nome (sem acento, pg_trgm), CNPJ parcial e CPF só exato; filtro por papel e
    /// "Inativos". O CPF sai mascarado do servidor.
    /// </summary>
    public async Task<PagedList<PartyListItem>> ListAsync(PartyListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(page);
        var parties = Filter(db.Parties.AsNoTracking(), query.Q);
        if (!query.IncludeInactive)
        {
            parties = parties.Where(p => p.IsActive);
        }

        if (query.Role is { } role)
        {
            parties = parties.Where(p => p.Roles.Any(r => r.Role == role));
        }

        parties = page.ParseSort() switch
        {
            ("createdAt", true) => parties.OrderByDescending(p => p.CreatedAt),
            ("createdAt", false) => parties.OrderBy(p => p.CreatedAt),
            ("name", true) => parties.OrderByDescending(p => p.SearchText),
            _ => parties.OrderBy(p => p.SearchText),
        };

        var paged = await parties.Select(p => new
        {
            p.Id, p.Kind, p.LegalName, p.TradeName, p.Document, p.IsActive,
            Roles = p.Roles.Select(r => r.Role).ToList(),
            Address = p.Addresses.Where(a => a.Kind == AddressKind.Billing).OrderByDescending(a => a.IsDefault).Select(a => new { a.City, a.State }).FirstOrDefault(),
        }).ToPagedListAsync(page, cancellationToken);
        return new PagedList<PartyListItem>(
            paged.Items.Select(p => new PartyListItem(p.Id, p.Kind, p.LegalName, p.TradeName, DisplayDocument(p.Document, full: false), p.Roles.Order().ToList(), p.Address?.City, p.Address?.State, p.IsActive)).ToList(),
            paged.Page, paged.PageSize, paged.Total);
    }

    /// <summary>Busca da topbar (CA 19): a partir de 3 caracteres, até 5 ativos, CPF mascarado.</summary>
    public async Task<IReadOnlyList<PartySearchHit>> SearchHitsAsync(string? q, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 3)
        {
            return [];
        }

        var hits = await Filter(db.Parties.AsNoTracking().Where(p => p.IsActive), q).OrderBy(p => p.SearchText).Take(5)
            .Select(p => new { p.Id, p.LegalName, p.TradeName, p.Document, Roles = p.Roles.Select(r => r.Role).ToList() }).ToListAsync(cancellationToken);
        return hits.Select(h => new PartySearchHit(h.Id, h.TradeName is { Length: > 0 } t ? $"{h.LegalName} ({t})" : h.LegalName, DisplayDocument(h.Document, full: false), h.Roles.Order().ToList())).ToList();
    }

    /// <summary>
    /// Termo: nome normalizado em qualquer parte; documento de PJ em qualquer parte (CNPJ é dado público); CPF só
    /// inteiro, com ou sem máscara: parte de um CPF não encontra nada (CA 17).
    /// </summary>
    private static IQueryable<Party> Filter(IQueryable<Party> parties, string? q)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return parties;
        }

        var text = Party.Normalize(q);
        var doc = BrazilianDocuments.Normalize(q);
        var exactCpf = doc.Length == 11 && doc.All(char.IsAsciiDigit) ? doc : null;
        var partialCnpj = doc.Length >= 3 ? doc : null;
        return parties.Where(p =>
            p.SearchText.Contains(text)
            || (partialCnpj != null && p.Kind == PartyKind.PJ && p.Document != null && p.Document.Contains(partialCnpj))
            || (exactCpf != null && p.Document == exactCpf));
    }
}
