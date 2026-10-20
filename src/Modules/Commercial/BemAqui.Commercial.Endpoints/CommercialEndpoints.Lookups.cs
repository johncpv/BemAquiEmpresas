using BemAqui.BuildingBlocks.Web;
using BemAqui.Commercial.Application;
using BemAqui.Parties.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Commercial.Endpoints;

internal static partial class CommercialEndpoints
{
    /// <summary>Seletores do orçamento; cargo e centro só para quem monta o custo.</summary>
    private static void MapLookups(RouteGroupBuilder lookups)
    {
        lookups.MapGet("/customers", async (string? q, QuoteLookups service, CancellationToken ct) => Results.Ok(await service.CustomersAsync(q, ct)))
            .RequirePermission("quotes.view").WithName("ListQuoteCustomers").Produces<IReadOnlyList<QuotePartyOption>>();
        lookups.MapGet("/installers", async (string? q, QuoteLookups service, CancellationToken ct) => Results.Ok(await service.InstallersAsync(q, ct)))
            .RequirePermission("quotes.view").WithName("ListQuoteInstallers").Produces<IReadOnlyList<QuotePartyOption>>();
        lookups.MapGet("/customers/{partyId:guid}/contacts", async (Guid partyId, QuoteLookups service, CancellationToken ct) => Results.Ok(await service.ContactsAsync(partyId, ct)))
            .RequirePermission("quotes.view").WithName("ListQuoteCustomerContacts").Produces<IReadOnlyList<PartyContactInfo>>();
        lookups.MapGet("/customers/{partyId:guid}/addresses", async (Guid partyId, QuoteLookups service, CancellationToken ct) => Results.Ok(await service.AddressesAsync(partyId, ct)))
            .RequirePermission("quotes.view").WithName("ListQuoteCustomerAddresses").Produces<IReadOnlyList<PartyAddressInfo>>();
        lookups.MapGet("/items", async (string? q, QuoteLookups service, CancellationToken ct) => Results.Ok(await service.ItemsAsync(q, ct)))
            .RequirePermission("quotes.view").WithName("ListQuoteItems").Produces<IReadOnlyList<QuoteItemOption>>();
        lookups.MapGet("/positions", async (QuoteLookups service, CancellationToken ct) => Results.Ok(await service.PositionsAsync(ct)))
            .RequirePermission("quotes.view_cost").WithName("ListQuotePositions").Produces<IReadOnlyList<LaborPositionOption>>();
        lookups.MapGet("/work-centers", async (QuoteLookups service, CancellationToken ct) => Results.Ok(await service.WorkCentersAsync(ct)))
            .RequirePermission("quotes.view_cost").WithName("ListQuoteWorkCenters").Produces<IReadOnlyList<LaborWorkCenterOption>>();
    }
}
