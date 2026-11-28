using BemAqui.BuildingBlocks.Authorization;
using BemAqui.Catalog.Contracts;
using BemAqui.Finance.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.HR.Contracts;
using BemAqui.Production.Contracts;

namespace BemAqui.Commercial.Application;

/// <summary>Seletores do orçamento pelos contratos dos donos (ADR 0006): cliente, contato, obra, item, cargo e centro.</summary>
public sealed class QuoteLookups(
    IParties parties, IItems items, IPositions positions, IHourlyCost hourlyCost, IWorkCenters workCenters, IPaymentTerms paymentTerms,
    IProductBarcodes barcodes, IItemCosts itemCosts, IPermissionChecker permissions)
{
    /// <summary>Teto da busca de item (PRC-00 CA 7): seletor de digitação, a tela pede para refinar quando vem cheio.</summary>
    public const int ItemLimit = 20;

    /// <summary>Termo da busca de item, em caracteres (PRC-00 CA 7).</summary>
    public const int TermMaxLength = 100;

    public async Task<IReadOnlyList<QuotePartyOption>> CustomersAsync(string? q, CancellationToken cancellationToken = default) =>
        (await parties.SearchAsync(q, PartyRoleKind.Customer, 20, cancellationToken))
            .Select(p => new QuotePartyOption(p.Id, p.TradeName ?? p.LegalName, p.Kind == PartyKind.PJ ? p.Document : null)).ToList();

    public async Task<IReadOnlyList<QuotePartyOption>> InstallersAsync(string? q, CancellationToken cancellationToken = default) =>
        (await parties.SearchAsync(q, PartyRoleKind.Installer, 20, cancellationToken))
            .Select(p => new QuotePartyOption(p.Id, p.TradeName ?? p.LegalName, p.Kind == PartyKind.PJ ? p.Document : null)).ToList();

    public Task<IReadOnlyList<PartyContactInfo>> ContactsAsync(Guid partyId, CancellationToken cancellationToken = default) => parties.ContactsAsync(partyId, cancellationToken);

    public Task<IReadOnlyList<PartyAddressInfo>> AddressesAsync(Guid partyId, CancellationToken cancellationToken = default) => parties.AddressesAsync(partyId, cancellationToken);

    /// <summary>
    /// Busca única de item (PRC-00 CA 1, 2 e 7): código e nome pelo <see cref="IItems"/> e, quando o termo parece um GTIN, o item do produto do
    /// Catálogo com esse código de barras (no topo). Só itens ativos, até <see cref="ItemLimit"/>. Custo médio só com <c>quotes.view_cost</c>.
    /// </summary>
    public async Task<IReadOnlyList<QuoteItemOption>> ItemsAsync(string? q, CancellationToken cancellationToken = default)
    {
        var term = q?.Trim();
        if (term is { Length: > TermMaxLength })
        {
            term = term[..TermMaxLength];
        }

        var found = (await items.SearchAsync(null, term, ItemLimit, cancellationToken)).ToList();
        if (!string.IsNullOrEmpty(term) && LooksLikeGtin(term))
        {
            var byGtin = (await barcodes.ItemIdsByGtinAsync(term, cancellationToken)).Where(id => found.All(f => f.Id != id)).ToList();
            if (byGtin.Count > 0)
            {
                found.InsertRange(0, (await items.FindAsync(byGtin, cancellationToken)).Where(i => i is { IsActive: true, IsDeleted: false }));
            }
        }

        found = found.Take(ItemLimit).ToList();
        var costs = found.Count > 0 && await permissions.HasPermissionAsync("quotes", "view_cost", cancellationToken)
            ? await itemCosts.AverageCostsAsync(found.Select(i => i.Id).ToList(), cancellationToken)
            : null;
        return found.Select(i => new QuoteItemOption(i.Id, i.Code, i.Name, i.UnitCode, i.Type, costs is null ? null : costs.GetValueOrDefault(i.Id))).ToList();
    }

    /// <summary>GTIN-8, 12, 13 ou 14: só dígitos, depois de tirar espaços e hífens.</summary>
    internal static bool LooksLikeGtin(string term)
    {
        var digits = new string(term.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        return digits.Length is 8 or 12 or 13 or 14 && digits.All(char.IsAsciiDigit);
    }

    /// <summary>Cargos ativos com o custo-hora (só para quem vê custo, na rota).</summary>
    public async Task<IReadOnlyList<LaborPositionOption>> PositionsAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<LaborPositionOption>();
        foreach (var p in await positions.ActiveAsync(cancellationToken))
        {
            list.Add(new LaborPositionOption(p.Id, p.Name, p.DefaultWorkCenterId, await hourlyCost.ForPositionAsync(p.Id, cancellationToken) ?? 0));
        }

        return list;
    }

    public async Task<IReadOnlyList<QuoteTermOption>> PaymentTermsAsync(CancellationToken cancellationToken = default) =>
        (await paymentTerms.ActiveAsync(cancellationToken)).Select(t => new QuoteTermOption(t.Id, t.Name)).ToList();

    public async Task<IReadOnlyList<LaborWorkCenterOption>> WorkCentersAsync(CancellationToken cancellationToken = default) =>
        (await workCenters.ActiveAsync(cancellationToken)).Select(w => new LaborWorkCenterOption(w.Id, w.Name)).ToList();
}
