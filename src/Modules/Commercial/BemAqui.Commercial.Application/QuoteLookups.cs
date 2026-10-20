using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.HR.Contracts;
using BemAqui.Production.Contracts;

namespace BemAqui.Commercial.Application;

/// <summary>Seletores do orçamento pelos contratos dos donos (ADR 0006): cliente, contato, obra, item, cargo e centro.</summary>
public sealed class QuoteLookups(IParties parties, IItems items, IPositions positions, IHourlyCost hourlyCost, IWorkCenters workCenters)
{
    public async Task<IReadOnlyList<QuotePartyOption>> CustomersAsync(string? q, CancellationToken cancellationToken = default) =>
        (await parties.SearchAsync(q, PartyRoleKind.Customer, 20, cancellationToken))
            .Select(p => new QuotePartyOption(p.Id, p.TradeName ?? p.LegalName, p.Kind == PartyKind.PJ ? p.Document : null)).ToList();

    public async Task<IReadOnlyList<QuotePartyOption>> InstallersAsync(string? q, CancellationToken cancellationToken = default) =>
        (await parties.SearchAsync(q, PartyRoleKind.Installer, 20, cancellationToken))
            .Select(p => new QuotePartyOption(p.Id, p.TradeName ?? p.LegalName, p.Kind == PartyKind.PJ ? p.Document : null)).ToList();

    public Task<IReadOnlyList<PartyContactInfo>> ContactsAsync(Guid partyId, CancellationToken cancellationToken = default) => parties.ContactsAsync(partyId, cancellationToken);

    public Task<IReadOnlyList<PartyAddressInfo>> AddressesAsync(Guid partyId, CancellationToken cancellationToken = default) => parties.AddressesAsync(partyId, cancellationToken);

    public async Task<IReadOnlyList<QuoteItemOption>> ItemsAsync(string? q, CancellationToken cancellationToken = default) =>
        (await items.SearchAsync(null, q, 20, cancellationToken)).Select(i => new QuoteItemOption(i.Id, i.Code, i.Name, i.UnitCode)).ToList();

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

    public async Task<IReadOnlyList<LaborWorkCenterOption>> WorkCentersAsync(CancellationToken cancellationToken = default) =>
        (await workCenters.ActiveAsync(cancellationToken)).Select(w => new LaborWorkCenterOption(w.Id, w.Name)).ToList();
}
